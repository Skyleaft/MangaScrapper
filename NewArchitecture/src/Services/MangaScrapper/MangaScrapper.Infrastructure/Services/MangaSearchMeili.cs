using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MangaScrapper.Infrastructure.Persistence;
using Mapster;
using Meilisearch;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Infrastructure.Services;

public class MangaSearchMeili(IOptions<MeiliConfig> config, ILogger<MangaSearchMeili> logger)
    : IMangaSearchRepository
{
    private const string IndexName = "mangas";
    private readonly MeilisearchClient _client = new(config.Value.Host, config.Value.MasterKey);

    public async Task<PagedList<Manga>> SearchAsync(string? search, List<string>? genres, string? status, string? type, string sortBy, string orderBy, int page,
        int pageSize, CancellationToken ct = default)
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
        logger.Log(LogLevel.Information, $"Found {paginated?.TotalHits} hits");

        var items = result.Hits.Select(MapToDomain).ToList();
        return new PagedList<Manga>(items, page, pageSize, totalHits);
    }
    
    private static Manga MapToDomain(MeiliMangaDocument doc)
    {
        return Manga.Reconstitute(
            MangaId.From(Guid.Parse(doc.Id)),
            doc.Title,
            doc.Author,
            doc.Type,
            0,
            doc.Genres,
            doc.Description,
            doc.ImageUrl,
            doc.LocalImageUrl,
            0,
            doc.Rating,
            doc.Popularity,
            0,
            doc.Status,
            DateTimeOffset.FromUnixTimeSeconds(doc.ReleaseDate).UtcDateTime,
            doc.TotalView,
            DateTimeOffset.FromUnixTimeSeconds(doc.CreatedAtTimestamp).UtcDateTime,
            DateTimeOffset.FromUnixTimeSeconds(doc.UpdatedAtTimestamp).UtcDateTime,
            "",
            null);
    }
}