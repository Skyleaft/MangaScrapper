using MangaScrapper.Infrastructure.Configuration;
using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MangaScrapper.Infrastructure.Services;

public class QdrantService
{
    private const string CollectionName = "mangas";
    private readonly QdrantClient _client;
    private readonly MangaMongoDbContext _dbContext;
    private readonly ILogger<QdrantService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingConfig _embeddingConfig;
    private const ulong VectorSize = 768; // Typical size for BgeBase

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

        var mangas = await _dbContext.Mangas
            .Find(_ => true)
            .ToListAsync(ct);

        if (mangas.Count == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync to Qdrant.");
            return;
        }

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

    public async Task UpsertMangaAsync(MangaDocument manga, CancellationToken ct = default)
    {
        var point = await MapToPointStructAsync(manga, ct);
        await _client.UpsertAsync(CollectionName, new[] { point }, cancellationToken: ct);
        _logger.LogInformation("Upserted manga '{Title}' (ID: {Id}) to Qdrant.", manga.Title, manga.Id);
    }

    public async Task DeleteMangaAsync(Guid id, CancellationToken ct = default)
    {
        ulong pointId = (ulong)id.GetHashCode();
        await _client.DeleteAsync(CollectionName, pointId, cancellationToken: ct);
        _logger.LogInformation("Deleted manga (ID: {Id}) from Qdrant.", id);
    }

    public async Task<List<Guid>> RecommendAsync(List<Guid> readingHistoryIds, int limit = 10, CancellationToken ct = default)
    {
        if (readingHistoryIds == null || !readingHistoryIds.Any())
            return new List<Guid>();

        var points = await _client.RetrieveAsync(
            CollectionName,
            readingHistoryIds.Select(id => (PointId)id).ToList(),
            withVectors: true,
            cancellationToken: ct);

        if (points.Count == 0) return new List<Guid>();

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

        var searchResult = await _client.SearchAsync(
            CollectionName,
            centroid,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    private async Task<PointStruct> MapToPointStructAsync(MangaDocument manga, CancellationToken ct = default)
    {
        float[] vector = new float[VectorSize];

        try
        {
            var text = $"{manga.Title} {manga.Description} {manga.Author} {string.Join(" ", manga.Genres ?? new List<string>())}";
            var httpClient = _httpClientFactory.CreateClient();
            var requestBody = new EmbedRequest { Text = text };
            var response = await httpClient.PostAsJsonAsync($"{_embeddingConfig.Host}/embed", requestBody, cancellationToken: ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
                if (result?.Vector != null && result.Vector.Count == (int)VectorSize)
                {
                    vector = result.Vector.ToArray();
                }
                else
                {
                    _logger.LogWarning("Embedding response null or invalid size for manga {Id}", manga.Id);
                }
            }
            else
            {
                _logger.LogError("Failed to get embedding from service. Status Code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting embedding for manga {Id}", manga.Id);
        }

        return new PointStruct
        {
            Id = (PointId)manga.Id,
            Vectors = vector,
            Payload =
            {
                ["title"] = manga.Title,
                ["description"] = manga.Description ?? string.Empty,
                ["author"] = manga.Author ?? "Unknown",
                ["status"] = manga.Status ?? "Unknown",
                ["type"] = manga.Type ?? "Unknown",
                ["genres"] = manga.Genres != null ? manga.Genres.ToArray() : Array.Empty<string>()
            }
        };
    }

    private sealed class EmbedRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class EmbedResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("vector")]
        public List<float> Vector { get; set; } = new();
    }
}
