using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Persistence;
using MangaScrapper.Core.Persistence.Documents;
using Mapster;
using Meilisearch;
using MongoDB.Driver;

namespace MangaScrapper.Core.Services;

public class MeilisearchService
{
    private const string IndexName = "mangas";
    private readonly MeilisearchClient _client;
    private readonly MangaMongoDbContext _dbContext;
    private readonly ILogger<MeilisearchService> _logger;

    public MeilisearchService(
        IOptions<MeiliConfig> config,
        MangaMongoDbContext dbContext,
        ILogger<MeilisearchService> logger)
    {
        _client = new MeilisearchClient(config.Value.Host, config.Value.MasterKey);
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates or gets the mangas index and configures searchable/filterable attributes.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Meilisearch index '{IndexName}'...", IndexName);

        var task = await _client.CreateIndexAsync(IndexName, "id", ct);
        await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);

        var index = _client.Index(IndexName);

        await index.UpdateSearchableAttributesAsync(new[]
        {
            "title",
            "author",
            "description",
            "genres"
        }, ct);

        await index.UpdateFilterableAttributesAsync(new[]
        {
            "type",
            "status",
            "genres",
            "rating",
            "popularity",
            "totalView",
            "releaseDate",
            "nsfw"
        }, ct);

        await index.UpdateSortableAttributesAsync(new[]
        {
            "title",
            "rating",
            "popularity",
            "totalView",
            "releaseDate",
            "createdAtTimestamp",
            "updatedAtTimestamp"
        }, ct);

        _logger.LogInformation("Meilisearch index '{IndexName}' initialized successfully.", IndexName);
    }

    /// <summary>
    /// Fetches all manga from MongoDB and syncs them to the Meilisearch index.
    /// </summary>
    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full sync from MongoDB to Meilisearch...");

        await InitializeAsync(ct);

        long totalCount = await _dbContext.Mangas.CountDocumentsAsync(_ => true, cancellationToken: ct);
        if (totalCount == 0)
        {
            _logger.LogWarning("No manga documents found in MongoDB. Nothing to sync.");
            return;
        }

        var index = _client.Index(IndexName);
        var projection = Builders<MangaDocument>.Projection.Exclude("Chapters.pages");

        using var cursor = await _dbContext.Mangas
            .Find(_ => true)
            .Project<MangaDocument>(projection)
            .ToCursorAsync(ct);

        int processed = 0;
        var batch = new List<MeiliMangaDocument>();

        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var doc in cursor.Current)
            {
                batch.Add(doc.Adapt<Manga>().Adapt<MeiliMangaDocument>());
                
                if (batch.Count >= 1000)
                {
                    var task = await index.AddDocumentsAsync(batch, "id", ct);
                    await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);
                    processed += batch.Count;
                    _logger.LogInformation("Indexed {Processed} of {Total} documents.", processed, totalCount);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            var task = await index.AddDocumentsAsync(batch, "id", ct);
            await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);
            processed += batch.Count;
            _logger.LogInformation("Indexed {Processed} of {Total} documents.", processed, totalCount);
        }

        _logger.LogInformation("Full sync completed. {Count} manga documents indexed.", processed);
    }

    /// <summary>
    /// Indexes a single manga (for real-time sync after create/update).
    /// </summary>
    public async Task IndexMangaAsync(Manga manga, CancellationToken ct = default)
    {
        var document = manga.Adapt<MeiliMangaDocument>();
        var index = _client.Index(IndexName);
        var task = await index.AddDocumentsAsync(new[] { document }, "id", ct);
        await _client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);

        _logger.LogInformation("Indexed manga '{Title}' (ID: {Id}) to Meilisearch.", manga.Title, manga.Id.Value);
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

    public async Task<MeiliMangaDocument?> SearchTitleAsync(string title, CancellationToken ct = default)
    {
        var index = _client.Index(IndexName);
        var searchQuery = new SearchQuery
        {
            AttributesToHighlight = new[] { "title" },
            ShowRankingScore = true,
            HitsPerPage = 1,
            Page = 1
        };

        var result = await index.SearchAsync<MeiliMangaDocument>(title, searchQuery, ct);
        return result.Hits.FirstOrDefault();
    }

    /// <summary>
    /// Searches the Meilisearch index with filters, sorting, and pagination.
    /// </summary>
    public async Task<(List<MeiliMangaDocument> Items, int TotalCount)> SearchAsync(
        string? search,
        List<string>? genres,
        string? status,
        string? type,
        string sortBy,
        string orderBy,
        int page,
        int pageSize,
        bool? nsfw = null,
        CancellationToken ct = default)
    {
        var index = _client.Index(IndexName);

        // Build filter expressions
        var filters = new List<string>();

        if (genres != null && genres.Count > 0)
        {
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

        if (nsfw.HasValue)
        {
            filters.Add($"nsfw = {nsfw.Value.ToString().ToLowerInvariant()}");
        }

        // Map sortBy to Meilisearch sort field
        var meiliSortField = sortBy.ToLowerInvariant() switch
        {
            "title" => "title",
            "createdat" => "createdAtTimestamp",
            "totalview" => "totalView",
            "popularity" => "popularity",
            "rating" => "rating",
            "releasedate" => "releaseDate",
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
}
