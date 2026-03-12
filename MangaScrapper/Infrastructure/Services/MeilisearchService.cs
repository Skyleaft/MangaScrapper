using Meilisearch;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using Meilisearch.QueryParameters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Services;

public class MeilisearchService
{
    private const string IndexName = "mangas";
    private readonly MeilisearchClient _client;
    private readonly MongoContext _mongoContext;
    private readonly ILogger<MeilisearchService> _logger;

    public MeilisearchService(
        IOptions<MeiliConfig> config,
        MongoContext mongoContext,
        ILogger<MeilisearchService> logger)
    {
        _client = new MeilisearchClient(config.Value.Host, config.Value.MasterKey);
        _mongoContext = mongoContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates or gets the mangas index and configures searchable/filterable attributes.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Meilisearch index '{IndexName}'...", IndexName);

        var task = await _client.CreateIndexAsync(IndexName, "id");
        await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);

        var index = _client.Index(IndexName);

        await index.UpdateSearchableAttributesAsync(new[]
        {
            "title",
            "author",
            "description",
            "genres"
        });

        await index.UpdateFilterableAttributesAsync(new[]
        {
            "type",
            "status",
            "genres",
            "rating",
            "popularity",
            "totalView"
        });

        await index.UpdateSortableAttributesAsync(new[]
        {
            "title",
            "rating",
            "popularity",
            "totalView",
            "createdAtTimestamp",
            "updatedAtTimestamp"
        });

        _logger.LogInformation("Meilisearch index '{IndexName}' initialized successfully.", IndexName);
    }

    /// <summary>
    /// Fetches all manga from MongoDB and syncs them to the Meilisearch index.
    /// </summary>
    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full sync from MongoDB to Meilisearch...");

        await InitializeAsync(ct);

        var mangas = await _mongoContext.Mangas
            .Find(_ => true)
            .ToListAsync(ct);

        if (mangas.Count == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync.");
            return;
        }

        var documents = mangas.Select(MapToMeiliDocument).ToList();

        // Batch upsert in chunks of 1000
        const int batchSize = 1000;
        var index = _client.Index(IndexName);

        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();
            var task = await index.AddDocumentsAsync(batch, "id", ct);
            await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);
            _logger.LogInformation("Indexed batch {Start}-{End} of {Total} documents.",
                i + 1, Math.Min(i + batchSize, documents.Count), documents.Count);
        }

        _logger.LogInformation("Full sync completed. {Count} manga documents indexed.", documents.Count);
    }

    /// <summary>
    /// Indexes a single manga document (for real-time sync after create/update).
    /// </summary>
    public async Task IndexMangaAsync(MangaDocument manga, CancellationToken ct = default)
    {
        var document = MapToMeiliDocument(manga);
        var index = _client.Index(IndexName);
        var task = await index.AddDocumentsAsync(new[] { document }, "id", ct);
        await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);

        _logger.LogInformation("Indexed manga '{Title}' (ID: {Id}) to Meilisearch.", manga.Title, manga.Id);
    }

    /// <summary>
    /// Removes a manga from the Meilisearch index.
    /// </summary>
    public async Task DeleteMangaAsync(Guid id, CancellationToken ct = default)
    {
        var index = _client.Index(IndexName);
        var task = await index.DeleteOneDocumentAsync(id.ToString(), ct);
        await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);

        _logger.LogInformation("Deleted manga (ID: {Id}) from Meilisearch index.", id);
    }

    public async Task<MeiliMangaDocument?> SearchTittleAsync(string title,CancellationToken ct = default)
    {
        var index = _client.Index(IndexName);
        var searchQuery = new SearchQuery
        {
            AttributesToHighlight = new []{"title"},
            ShowRankingScore = true,
            HitsPerPage = 1,
            Page = 1
        };

        var result = await index.SearchAsync<MeiliMangaDocument>(title, searchQuery, ct);
        
        return result.Hits.FirstOrDefault()!;
    }

    /// <summary>
    /// Searches the Meilisearch index with filters, sorting, and pagination.
    /// </summary>
    public async Task<(List<MeiliMangaDocument> Items, long TotalCount)> SearchAsync(
        string? search,
        List<string>? genres,
        string? status,
        string? type,
        string sortBy,
        string orderBy,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var index = _client.Index(IndexName);

        // Build filter expressions
        var filters = new List<string>();

        if (genres != null && genres.Count > 0)
        {
            // Each genre must match (AND logic)
            foreach (var genre in genres)
            {
                filters.Add($"genres = \"{genre}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add($"status = \"{status}\"");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filters.Add($"type = \"{type}\"");
        }

        // Map sortBy to Meilisearch sort field
        var meiliSortField = sortBy.ToLowerInvariant() switch
        {
            "title" => "title",
            "createdat" => "createdAtTimestamp",
            "totalview" => "totalView",
            "popularity" => "popularity",
            "rating" => "rating",
            _ => "updatedAtTimestamp" // default: updatedAt
        };

        var sortDirection = orderBy?.ToLowerInvariant() == "asc" ? "asc" : "desc";

        var searchQuery = new SearchQuery
        {
            Filter = filters.Count > 0 ? string.Join(" AND ", filters) : null,
            Sort = new[] { $"{meiliSortField}:{sortDirection}" },
            HitsPerPage = pageSize,
            Page = page
        };

        var result = await index.SearchAsync<MeiliMangaDocument>(search ?? "", searchQuery, ct);

        var paginated = result as PaginatedSearchResult<MeiliMangaDocument>;
        var totalHits = paginated?.TotalHits ?? 0;

        return (result.Hits.ToList(), totalHits);
    }

    private static MeiliMangaDocument MapToMeiliDocument(MangaDocument manga)
    {
        return new MeiliMangaDocument
        {
            Id = manga.Id.ToString(),
            Title = manga.Title,
            Author = manga.Author,
            Type = manga.Type,
            Genres = manga.Genres ?? new List<string>(),
            Description = manga.Description,
            Status = manga.Status,
            Rating = manga.Rating,
            Popularity = manga.Popularity,
            TotalView = manga.TotalView,
            ImageUrl = manga.ImageUrl,
            LocalImageUrl = manga.LocalImageUrl,
            TotalChapters = manga.Chapters?.Count ?? 0,
            LatestChapterNumber = manga.Chapters?.MaxBy(c => c.Number)?.Number ?? 0,
            CreatedAtTimestamp = ((DateTimeOffset)manga.CreatedAt.ToUniversalTime()).ToUnixTimeSeconds(),
            UpdatedAtTimestamp = ((DateTimeOffset)manga.UpdatedAt.ToUniversalTime()).ToUnixTimeSeconds()
        };
    }
}
