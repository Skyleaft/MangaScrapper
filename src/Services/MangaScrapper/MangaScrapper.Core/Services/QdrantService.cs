using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Persistence;
using MangaScrapper.Core.Persistence.Documents;
using Mapster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.Net.Http.Json;

namespace MangaScrapper.Core.Services;

public class QdrantService
{
    private const string CollectionName = "mangas";
    public const string DenseVectorName = "dense";
    public const string SparseVectorName = "sparse";

    private readonly QdrantClient _client;
    private readonly MangaMongoDbContext _dbContext;
    private readonly ILogger<QdrantService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingConfig _embeddingConfig;
    private const ulong VectorSize = 1024; // BAAI/bge-m3 produces 1024-dim dense vectors

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
        _logger.LogInformation("Initializing Qdrant collection '{CollectionName}' with Hybrid vectors...", CollectionName);

        var collections = await _client.ListCollectionsAsync(cancellationToken: ct);
        if (!collections.Contains(CollectionName))
        {
            await _client.CreateCollectionAsync(
                CollectionName,
                vectorsConfig: new VectorParamsMap
                {
                    Map =
                    {
                        [DenseVectorName] = new VectorParams
                        {
                            Size = VectorSize,
                            Distance = Distance.Cosine
                        }
                    }
                },
                sparseVectorsConfig: new SparseVectorConfig
                {
                    Map =
                    {
                        [SparseVectorName] = new SparseVectorParams()
                    }
                },
                cancellationToken: ct);
            _logger.LogInformation("Qdrant collection '{CollectionName}' created successfully with hybrid vectors.", CollectionName);
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

        long totalCount = await _dbContext.Mangas.CountDocumentsAsync(_ => true, cancellationToken: ct);
        if (totalCount == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync to Qdrant.");
            return;
        }

        var projection = Builders<MangaDocument>.Projection.Exclude(x => x.Chapters);
        using var cursor = await _dbContext.Mangas
            .Find(_ => true)
            .Project<MangaDocument>(projection)
            .ToCursorAsync(ct);

        int processed = 0;
        var batch = new List<PointStruct>();

        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var doc in cursor.Current)
            {
                var manga = doc.Adapt<Manga>();
                batch.Add(await MapToPointStructAsync(manga, ct));

                if (batch.Count >= 500)
                {
                    await _client.UpsertAsync(CollectionName, batch, cancellationToken: ct);
                    processed += batch.Count;
                    _logger.LogInformation("Qdrant synced {Processed} of {Total} documents.", processed, totalCount);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            await _client.UpsertAsync(CollectionName, batch, cancellationToken: ct);
            processed += batch.Count;
            _logger.LogInformation("Qdrant synced {Processed} of {Total} documents.", processed, totalCount);
        }

        _logger.LogInformation("Full sync completed. {Count} manga documents synced to Qdrant.", processed);
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
    /// History-based recommendation: computes centroid of reading history dense vectors and
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
            .Select(ExtractDenseVector)
            .Where(v => v != null && v.Length == (int)VectorSize)
            .Select(v => v!)
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
            usingVector: DenseVectorName,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Hybrid similarity search seeded from a single manga (Dense + Sparse vectors with RRF fusion).
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

        var targetPoint = points[0];
        var denseData = ExtractDenseVector(targetPoint);
        var sparseData = ExtractSparseVector(targetPoint);

        if (denseData == null || denseData.Length == 0)
        {
            _logger.LogWarning("No dense vector found for manga (ID: {Id}) in Qdrant.", mangaId);
            return new List<Guid>();
        }

        var filter = new Filter();
        filter.MustNot.Add(new Condition
        {
            HasId = new HasIdCondition { HasId = { (PointId)mangaId } }
        });

        var prefetchList = new List<PrefetchQuery>
        {
            new()
            {
                Query = new Query { Nearest = new VectorInput(denseData.ToArray()) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            }
        };

        if (sparseData != null && sparseData.Indices.Count > 0)
        {
            var sparseVector = new SparseVector();
            sparseVector.Indices.AddRange(sparseData.Indices);
            sparseVector.Values.AddRange(sparseData.Values);

            prefetchList.Add(new PrefetchQuery
            {
                Query = new Query
                {
                    Nearest = new VectorInput { Sparse = sparseVector }
                },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            });
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Hybrid multilingual semantic text search (Dense semantic + Sparse lexical matching with RRF fusion).
    /// </summary>
    public async Task<List<Guid>> SemanticSearchAsync(string queryText, int limit = 10, CancellationToken ct = default)
    {
        var embeddingResult = await GetEmbeddingAsync(queryText, mode: "query", ct);
        if (embeddingResult == null)
        {
            _logger.LogWarning("Failed to get embedding for semantic search query.");
            return new List<Guid>();
        }

        var prefetchList = new List<PrefetchQuery>
        {
            new()
            {
                Query = new Query { Nearest = new VectorInput(embeddingResult.Dense) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 2)
            }
        };

        if (embeddingResult.Sparse != null && embeddingResult.Sparse.Indices.Any())
        {
            var sparseVector = new SparseVector();
            sparseVector.Indices.AddRange(embeddingResult.Sparse.Indices.Select(i => (uint)i));
            sparseVector.Values.AddRange(embeddingResult.Sparse.Values);

            prefetchList.Add(new PrefetchQuery
            {
                Query = new Query
                {
                    Nearest = new VectorInput { Sparse = sparseVector }
                },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 2)
            });
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Filtered hybrid vector similarity search seeded from a single manga.
    /// Applies Qdrant payload filters (status, type, genres) with RRF fusion.
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

        var targetPoint = points[0];
        var denseData = ExtractDenseVector(targetPoint);
        var sparseData = ExtractSparseVector(targetPoint);

        if (denseData == null || denseData.Length == 0)
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

        var prefetchList = new List<PrefetchQuery>
        {
            new()
            {
                Query = new Query { Nearest = new VectorInput(denseData.ToArray()) },
                Using = DenseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            }
        };

        if (sparseData != null && sparseData.Indices.Count > 0)
        {
            var sparseVector = new SparseVector();
            sparseVector.Indices.AddRange(sparseData.Indices);
            sparseVector.Values.AddRange(sparseData.Values);

            prefetchList.Add(new PrefetchQuery
            {
                Query = new Query
                {
                    Nearest = new VectorInput { Sparse = sparseVector }
                },
                Using = SparseVectorName,
                Limit = (ulong)(limit * 2),
                Filter = filter
            });
        }

        var searchResult = await _client.QueryAsync(
            CollectionName,
            prefetch: prefetchList,
            query: new Query { Fusion = Fusion.Rrf },
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    /// <summary>
    /// Advanced recommendation using Qdrant's native positive/negative example API on dense vectors.
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
            usingVector: DenseVectorName,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return result.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private float[]? ExtractDenseVector(RetrievedPoint point)
    {
        if (point.Vectors == null)
            return null;

        // 1. Try NamedVectors (hybrid collection format: point.Vectors.Vectors.Vectors)
        var map = point.Vectors.Vectors?.Vectors;
        if (map != null && map.TryGetValue(DenseVectorName, out var namedVector))
        {
            var dense = namedVector.GetDenseVector();
            if (dense?.Data != null && dense.Data.Count > 0)
                return dense.Data.ToArray();

            var data = namedVector.Data;
            if (data != null && data.Count > 0)
                return data.ToArray();
        }

        // 2. Try single default vector (fallback for legacy or un-named collections)
        if (point.Vectors.Vector != null)
        {
            var singleDense = point.Vectors.Vector.GetDenseVector();
            if (singleDense?.Data != null && singleDense.Data.Count > 0)
                return singleDense.Data.ToArray();

            var singleData = point.Vectors.Vector.Data;
            if (singleData != null && singleData.Count > 0)
                return singleData.ToArray();
        }

        return null;
    }

    private SparseVector? ExtractSparseVector(RetrievedPoint point)
    {
        if (point.Vectors == null)
            return null;

        var map = point.Vectors.Vectors?.Vectors;
        if (map != null && map.TryGetValue(SparseVectorName, out var namedVector))
        {
            var sparse = namedVector.GetSparseVector();
            if (sparse != null && sparse.Indices.Count > 0)
                return sparse;

            if (namedVector.Sparse != null && namedVector.Sparse.Indices.Count > 0)
                return namedVector.Sparse;
        }

        return null;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<PointStruct> MapToPointStructAsync(Manga manga, CancellationToken ct = default)
    {
        var distinctGenres = (manga.Genres ?? new List<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var distinctCategories = (manga.Categories ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15);

        var textParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manga.Title))
            textParts.Add($"Title: {manga.Title}");
        if (!string.IsNullOrWhiteSpace(manga.Author) && manga.Author != "Unknown")
            textParts.Add($"Author: {manga.Author}");
        
        var genresStr = string.Join(", ", distinctGenres);
        if (!string.IsNullOrWhiteSpace(genresStr))
            textParts.Add($"Genres: {genresStr}");

        var categoriesStr = string.Join(", ", distinctCategories);
        if (!string.IsNullOrWhiteSpace(categoriesStr))
            textParts.Add($"Themes: {categoriesStr}");

        if (!string.IsNullOrWhiteSpace(manga.Description))
            textParts.Add($"Synopsis: {manga.Description}");

        var text = string.Join(". ", textParts);
        var embedding = await GetEmbeddingAsync(text, mode: "passage", ct);

        var namedVectors = new NamedVectors();
        if (embedding?.Dense != null && embedding.Dense.Length == (int)VectorSize)
        {
            var denseVec = new Vector();
            denseVec.Data.AddRange(embedding.Dense);
            namedVectors.Vectors[DenseVectorName] = denseVec;
        }
        else
        {
            _logger.LogWarning("Using zero dense vector for manga {Id} due to embedding failure.", manga.Id.Value);
            var zeroVec = new Vector();
            zeroVec.Data.AddRange(new float[VectorSize]);
            namedVectors.Vectors[DenseVectorName] = zeroVec;
        }

        if (embedding?.Sparse != null && embedding.Sparse.Indices.Any())
        {
            var sparseVec = new SparseVector();
            sparseVec.Indices.AddRange(embedding.Sparse.Indices.Select(i => (uint)i));
            sparseVec.Values.AddRange(embedding.Sparse.Values);

            namedVectors.Vectors[SparseVectorName] = new Vector { Sparse = sparseVec };
        }

        return new PointStruct
        {
            Id = (PointId)manga.Id.Value,
            Vectors = new Vectors { Vectors_ = namedVectors },
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
    /// Calls the embedding microservice to get both dense and sparse representations.
    /// </summary>
    private async Task<EmbeddingResult?> GetEmbeddingAsync(string text, string mode, CancellationToken ct)
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
            if (result?.Dense != null && result.Dense.Count == (int)VectorSize)
            {
                return new EmbeddingResult
                {
                    Dense = result.Dense.ToArray(),
                    Sparse = result.Sparse
                };
            }

            _logger.LogWarning("Embedding response null or wrong size (expected {Expected}, got {Got}).",
                VectorSize, result?.Dense?.Count ?? 0);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling embedding service (mode={Mode}).", mode);
            return null;
        }
    }

    private sealed class EmbeddingResult
    {
        public float[] Dense { get; set; } = Array.Empty<float>();
        public SparseVectorResponse? Sparse { get; set; }
    }

    private sealed class EmbedRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("mode")]
        public string Mode { get; set; } = "passage";
    }

    private sealed class SparseVectorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("indices")]
        public List<int> Indices { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("values")]
        public List<float> Values { get; set; } = new();
    }

    private sealed class EmbedResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("dense")]
        public List<float> Dense { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("sparse")]
        public SparseVectorResponse? Sparse { get; set; }
    }
}
