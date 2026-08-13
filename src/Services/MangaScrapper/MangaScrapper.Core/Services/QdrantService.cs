using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Persistence;
using Mapster;
using MongoDB.Driver;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MangaScrapper.Core.Services;

public class QdrantService
{
    private const string CollectionName = "mangas";
    private readonly QdrantClient _client;
    private readonly MangaMongoDbContext _dbContext;
    private readonly ILogger<QdrantService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingConfig _embeddingConfig;
    private const ulong VectorSize = 768; // multilingual-e5-base produces 768-dim vectors

    public QdrantService(
        IOptions<QdrantConfig> config,
        MangaMongoDbContext dbContext,
        ILogger<QdrantService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<EmbeddingConfig> embeddingConfig)
    {
        var host = config.Value.Host;
        bool isHttps = false;

        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            isHttps = true;
            host = host.Substring(8);
        }
        else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            host = host.Substring(7);
        }

        var port = config.Value.Port;
        if (port == 6333)
        {
            // Auto-correct to gRPC port if REST port is provided
            port = 6334;
        }

        _client = new QdrantClient(host, port: port, https: isHttps, apiKey: config.Value.ApiKey);
        _dbContext = dbContext;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _embeddingConfig = embeddingConfig.Value;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Qdrant collection '{CollectionName}'...", CollectionName);

        var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
        if (!collections.Contains(CollectionName))
        {
            await _client.CreateCollectionAsync(
                CollectionName,
                new VectorParams
                {
                    Size = VectorSize,
                    Distance = Distance.Cosine
                },
                cancellationToken: ct);
            _logger.LogInformation("Qdrant collection '{CollectionName}' created successfully.", CollectionName);
        }
        else
        {
            _logger.LogInformation("Qdrant collection '{CollectionName}' already exists.", CollectionName);
        }
    }

    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full sync from MongoDB to Qdrant...");

        await InitializeAsync(ct);

        var mangaDocs = await _dbContext.Mangas
            .Find(_ => true)
            .ToListAsync(ct);

        if (mangaDocs.Count == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync to Qdrant.");
            return;
        }

        var mangas = mangaDocs.Select(doc => doc.Adapt<Manga>()).ToList();

        const int batchSize = 500;
        for (int i = 0; i < mangas.Count; i += batchSize)
        {
            var batch = mangas.Skip(i).Take(batchSize).ToList();
            var points = new List<PointStruct>();
            foreach (var manga in batch)
            {
                points.Add(await MapToPointStructAsync(manga, ct));
            }

            await _client.UpsertAsync(CollectionName, points, cancellationToken: ct);

            _logger.LogInformation("Qdrant synced batch {Start}-{End} of {Total} documents.",
                i + 1, Math.Min(i + batchSize, mangas.Count), mangas.Count);
        }

        _logger.LogInformation("Full sync completed. {Count} manga documents synced to Qdrant.", mangas.Count);
    }

    public async Task UpsertMangaAsync(Manga manga, CancellationToken ct = default)
    {
        var point = await MapToPointStructAsync(manga, ct);
        await _client.UpsertAsync(CollectionName, new[] { point }, cancellationToken: ct);
        _logger.LogInformation("Upserted manga '{Title}' (ID: {Id}) to Qdrant.", manga.Title, manga.Id.Value);
    }

    public async Task DeleteMangaAsync(Guid id, CancellationToken ct = default)
    {
        ulong pointId = (ulong)id.GetHashCode();
        await _client.DeleteAsync(CollectionName, pointId, cancellationToken: ct);
        _logger.LogInformation("Deleted manga (ID: {Id}) from Qdrant.", id);
    }

    /// <summary>
    /// History-based recommendation: computes centroid of reading history vectors and
    /// returns nearest neighbors, excluding already-read manga.
    /// </summary>
    public async Task<List<Guid>> RecommendAsync(List<Guid> readingHistoryIds, int limit = 10, CancellationToken ct = default)
    {
        if (readingHistoryIds == null || !readingHistoryIds.Any())
            return new List<Guid>();

        var points = await _client.RetrieveAsync(
            CollectionName,
            readingHistoryIds.Select(id => (PointId)id).ToList(),
            withVectors: true,
            cancellationToken: ct);

        if (points.Count == 0)
        {
            _logger.LogWarning("None of the provided reading history IDs were found in Qdrant.");
            return new List<Guid>();
        }

        var denseVectors = points
            .Where(p => p.Vectors?.Vector?.Dense != null)
            .Select(p => p.Vectors.Vector.Dense.Data)
            .ToList();

        if (!denseVectors.Any())
        {
            _logger.LogWarning("No valid dense vectors found for provided IDs.");
            return new List<Guid>();
        }

        // Compute centroid (Mean Vector)
        var centroid = new float[VectorSize];
        foreach (var vector in denseVectors)
        {
            for (int i = 0; i < (int)VectorSize; i++)
            {
                centroid[i] += vector[i];
            }
        }

        for (int i = 0; i < (int)VectorSize; i++)
        {
            centroid[i] /= denseVectors.Count;
        }

        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { readingHistoryIds.Select(id => (PointId)id) } }
        });

        var searchResult = await _client.QueryAsync(
            CollectionName,
            query: new Query { Nearest = new VectorInput(centroid) },
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Vector similarity search seeded from a single manga, optionally excluding it.
    /// </summary>
    public async Task<List<Guid>> SearchSimilarAsync(Guid mangaId, int limit = 10, CancellationToken ct = default)
    {
        var points = await _client.RetrieveAsync(
            CollectionName,
            new List<PointId> { (PointId)mangaId },
            withVectors: true,
            cancellationToken: ct);

        if (points.Count == 0)
        {
            _logger.LogWarning("Manga (ID: {Id}) not found in Qdrant. Cannot compute similar mangas.", mangaId);
            return new List<Guid>();
        }

        var sourceVector = points[0].Vectors?.Vector?.Dense?.Data;
        if (sourceVector == null || sourceVector.Count == 0)
        {
            _logger.LogWarning("No dense vector found for manga (ID: {Id}) in Qdrant.", mangaId);
            return new List<Guid>();
        }

        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { (PointId)mangaId } }
        });

        var searchResult = await _client.QueryAsync(
            CollectionName,
            query: new Query { Nearest = new VectorInput(sourceVector.ToArray()) },
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Multilingual semantic text search. Embeds the query with mode=query (e5 prefix convention)
    /// and returns nearest-neighbor manga by cosine similarity. Supports 100+ languages including Indonesian.
    /// </summary>
    public async Task<List<Guid>> SemanticSearchAsync(string queryText, int limit = 10, CancellationToken ct = default)
    {
        // mode=query applies "query: " prefix required by multilingual-e5 for retrieval
        var vector = await GetEmbeddingAsync(queryText, mode: "query", ct);
        if (vector == null)
        {
            _logger.LogWarning("Failed to get embedding for semantic search query.");
            return new List<Guid>();
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            query: new Query { Nearest = new VectorInput(vector) },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Filtered vector similarity search seeded from a single manga.
    /// Applies Qdrant payload filters (status, type, genres) before nearest-neighbor search.
    /// </summary>
    public async Task<List<Guid>> SearchSimilarFilteredAsync(
        Guid mangaId,
        string? status,
        string? type,
        List<string>? genres,
        int limit = 10,
        CancellationToken ct = default)
    {
        var points = await _client.RetrieveAsync(
            CollectionName,
            new List<PointId> { (PointId)mangaId },
            withVectors: true,
            cancellationToken: ct);

        if (points.Count == 0)
        {
            _logger.LogWarning("Manga (ID: {Id}) not found in Qdrant for filtered similarity.", mangaId);
            return new List<Guid>();
        }

        var sourceVector = points[0].Vectors?.Vector?.Dense?.Data;
        if (sourceVector == null || sourceVector.Count == 0)
        {
            _logger.LogWarning("No dense vector found for manga (ID: {Id}) in Qdrant.", mangaId);
            return new List<Guid>();
        }

        var filter = new Filter();

        // Always exclude the source manga
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { (PointId)mangaId } }
        });

        // Apply payload field filters
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "status",
                    Match = new Match { Keyword = status }
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "type",
                    Match = new Match { Keyword = type }
                }
            });
        }

        if (genres != null && genres.Any())
        {
            foreach (var genre in genres)
            {
                filter.Must.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "genres",
                        Match = new Match { Keyword = genre }
                    }
                });
            }
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            query: new Query { Nearest = new VectorInput(sourceVector.ToArray()) },
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Advanced recommendation using Qdrant's native positive/negative example API.
    /// Pulls results toward liked manga and away from disliked manga server-side.
    /// No embedding calls needed — Qdrant handles vector arithmetic internally.
    /// </summary>
    public async Task<List<Guid>> RecommendAdvancedAsync(
        List<Guid> likedIds,
        List<Guid> dislikedIds,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (!likedIds.Any())
            return new List<Guid>();

        var positives = likedIds.Select(id => (PointId)id).ToList();
        var negatives = dislikedIds.Select(id => (PointId)id).ToList();

        // Exclude all input IDs from results
        var excludedIds = likedIds.Concat(dislikedIds).Select(id => (PointId)id);
        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { excludedIds } }
        });

        var recommend = new RecommendInput();
        recommend.Positive.AddRange(positives.Select(p => (VectorInput)p));
        recommend.Negative.AddRange(negatives.Select(n => (VectorInput)n));

        var result = await _client.QueryAsync(
            CollectionName,
            query: recommend,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return result.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<PointStruct> MapToPointStructAsync(Manga manga, CancellationToken ct = default)
    {
        float[] vector = new float[VectorSize];

        var text = $"{manga.Title} {manga.Description} {manga.Author} {string.Join(" ", manga.Genres ?? new List<string>())} {string.Join(" ", manga.Categories ?? new List<string>())}";
        // mode=passage applies "passage: " prefix for e5 indexing convention
        var embedding = await GetEmbeddingAsync(text, mode: "passage", ct);
        if (embedding != null)
        {
            vector = embedding;
        }
        else
        {
            _logger.LogWarning("Using zero vector for manga {Id} due to embedding failure.", manga.Id.Value);
        }

        return new PointStruct
        {
            Id = (PointId)manga.Id.Value,
            Vectors = vector,
            Payload =
            {
                ["title"] = manga.Title,
                ["description"] = manga.Description ?? string.Empty,
                ["author"] = manga.Author ?? "Unknown",
                ["status"] = manga.Status ?? "Unknown",
                ["type"] = manga.Type ?? "Unknown",
                ["genres"] = manga.Genres != null ? manga.Genres.ToArray() : Array.Empty<string>(),
                ["categories"] = manga.Categories != null ? manga.Categories.ToArray() : Array.Empty<string>()
            }
        };
    }

    /// <summary>
    /// Calls the embedding microservice.
    /// mode: "passage" for indexing (adds "passage: " prefix), "query" for search (adds "query: " prefix).
    /// Returns null on failure so callers can apply fallback behaviour.
    /// </summary>
    private async Task<float[]?> GetEmbeddingAsync(string text, string mode, CancellationToken ct)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var requestBody = new EmbedRequest { Text = text, Mode = mode };
            var response = await httpClient.PostAsJsonAsync($"{_embeddingConfig.Host}/embed", requestBody, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Embedding service returned {StatusCode} for mode={Mode}.", response.StatusCode, mode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
            if (result?.Vector != null && result.Vector.Count == (int)VectorSize)
                return result.Vector.ToArray();

            _logger.LogWarning("Embedding response null or wrong size (expected {Expected}, got {Got}).",
                VectorSize, result?.Vector?.Count ?? 0);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling embedding service (mode={Mode}).", mode);
            return null;
        }
    }

    private sealed class EmbedRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("mode")]
        public string Mode { get; set; } = "passage";
    }

    private sealed class EmbedResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("vector")]
        public List<float> Vector { get; set; } = new();
    }
}
