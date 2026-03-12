using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
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
    private readonly MongoContext _mongoContext;
    private readonly ILogger<QdrantService> _logger;
    private const ulong VectorSize = 384; // Typical size for MiniLM

    public QdrantService(
        IOptions<QdrantConfig> config,
        MongoContext mongoContext,
        ILogger<QdrantService> logger)
    {
        _client = new QdrantClient(config.Value.Host,port:config.Value.Port, apiKey: config.Value.ApiKey);
        _mongoContext = mongoContext;
        _logger = logger;
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

        var mangas = await _mongoContext.Mangas
            .Find(_ => true)
            .ToListAsync(ct);

        if (mangas.Count == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync to Qdrant.");
            return;
        }

        // Batch upsert in chunks of 500
        const int batchSize = 500;

        for (int i = 0; i < mangas.Count; i += batchSize)
        {
            var batch = mangas.Skip(i).Take(batchSize).ToList();
            var points = batch.Select(MapToPointStruct).ToList();

            await _client.UpsertAsync(CollectionName, points, cancellationToken: ct);
            
            _logger.LogInformation("Qdrant synced batch {Start}-{End} of {Total} documents.",
                i + 1, Math.Min(i + batchSize, mangas.Count), mangas.Count);
        }

        _logger.LogInformation("Full sync completed. {Count} manga documents synced to Qdrant.", mangas.Count);
    }

    public async Task UpsertMangaAsync(MangaDocument manga, CancellationToken ct = default)
    {
        var point = MapToPointStruct(manga);
        await _client.UpsertAsync(CollectionName, new[] { point }, cancellationToken: ct);
        _logger.LogInformation("Upserted manga '{Title}' (ID: {Id}) to Qdrant.", manga.Title, manga.Id);
    }

    public async Task DeleteMangaAsync(Guid id, CancellationToken ct = default)
    {
        await _client.DeleteAsync(CollectionName, new[] { (PointId)id }, cancellationToken: ct);
        _logger.LogInformation("Deleted manga (ID: {Id}) from Qdrant.", id);
    }

    public async Task<List<Guid>> RecommendAsync(List<Guid> readingHistoryIds, int limit = 10, CancellationToken ct = default)
    {
        if (readingHistoryIds == null || readingHistoryIds.Count == 0)
        {
            return new List<Guid>();
        }

        var points = await _client.RetrieveAsync(CollectionName, readingHistoryIds.Select(id => (PointId)id).ToArray(), withVectors: true, cancellationToken: ct);

        if (points.Count == 0)
        {
            return new List<Guid>();
        }
        
        foreach (var p in points)
        {
            if (p.Vectors?.Vector?.Data == null)
            {
                _logger.LogWarning("Point {Id} has no vector", p.Id);
            }
            else
            {
                _logger.LogInformation("Point {Id} vector size: {Size}", p.Id, p.Vectors.Vector.Data.Count);
            }
        }

        // 2. Compute a simple centroid (mean vector)
        var vectors = points
            .Where(p => p.Vectors?.Vector?.Data?.Count > 0)
            .Select(p => p.Vectors.Vector.Data.ToArray())
            .ToList();

        if (vectors.Count == 0)
        {
            return new List<Guid>();
        }

        var dimension = vectors[0].Length;
        var centroid = new float[dimension];

        foreach (var vector in vectors)
        {
            for (int i = 0; i < dimension; i++)
            {
                centroid[i] += vector[i];
            }
        }

        for (int i = 0; i < dimension; i++)
        {
            centroid[i] /= vectors.Count;
        }

        // 3. Query Qdrant with the centroid, filtering out the already read IDs
        var filter = new Filter();
        var hasIdCondition = new HasIdCondition();
        hasIdCondition.HasId.AddRange(readingHistoryIds.Select(id => (PointId)id));

        filter.MustNot.Add(new Condition
        {
            HasId = hasIdCondition
        });

        var searchResult = await _client.SearchAsync(
            CollectionName,
            centroid,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct
        );

        return searchResult.Select(r => Guid.Parse(r.Id.Uuid)).ToList();
    }

    private PointStruct MapToPointStruct(MangaDocument manga)
    {
        // 🚨 Placeholder vector generator 🚨
        // As discussed in the implementation plan, you'll need a real embedding model.
        // For now, we seed with zeroes to establish the pipeline.
        var dummyVector = new float[VectorSize];

        var point = new PointStruct
        {
            Id = (PointId)manga.Id,
            Vectors = dummyVector,
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

        return point;
    }
}
