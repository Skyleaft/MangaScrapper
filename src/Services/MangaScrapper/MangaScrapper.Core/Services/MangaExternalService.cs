using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Persistence.Documents;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Core.Services;

public class MangaExternalService(ILogger<MangaExternalService> logger, 
    MeilisearchService meilisearchService,
    QdrantService qdrantService,
    IMangaRepository mangaRepository)
    : IMangaExternalRepository
{


    public async Task<PagedList<Manga>> SearchAsync(string? search, List<string>? genres, string? status, string? type, string sortBy, string orderBy, int page,
        int pageSize, CancellationToken ct = default)
    {
        var data = await meilisearchService.SearchAsync(search, genres, status, type, sortBy, orderBy, page, pageSize, ct);
        var items = data.Items.Select(MapToDomain).ToList();
        return new PagedList<Manga>(items, page, pageSize, data.TotalCount);
    }

    public async Task<List<Manga>> GetRecomendationAsync(List<Guid> readingHistoryIds, int limit, CancellationToken ct = default)
    {
        var recList = await qdrantService.RecommendAsync(readingHistoryIds, limit, ct);
        var mangas = await mangaRepository.GetByIdsAsync(recList,ct);
        return mangas;
    }

    private static Manga MapToDomain(MeiliMangaDocument doc)
    {
        return Manga.Reconstitute(
            MangaId.From(Guid.Parse(doc.Id)),
            doc.Title,
            doc.Author,
            doc.Type,
            0,
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

    public async Task IndexMangaAsync(Manga manga, CancellationToken ct = default)
    {
        var document = new MangaDocument
        {
            Id = manga.Id.Value,
            MalID = manga.MalId,
            AnilistID = manga.AnilistId,
            Title = manga.Title,
            Author = manga.Author,
            Type = manga.Type,
            Genres = manga.Genres,
            Description = manga.Description,
            ImageUrl = manga.ImageUrl,
            LocalImageUrl = manga.LocalImageUrl,
            ThumbnailSize = manga.ThumbnailSize,
            Rating = manga.Rating,
            Popularity = manga.Popularity,
            ReleaseDate = manga.ReleaseDate,
            Status = manga.Status,
            CreatedAt = manga.CreatedAt,
            UpdatedAt = manga.UpdatedAt,
            Url = manga.Url
        };

        await meilisearchService.IndexMangaAsync(document, ct);
    }
}