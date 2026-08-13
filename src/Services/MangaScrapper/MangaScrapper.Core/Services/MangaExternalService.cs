using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.ValueObjects;
using Mapster;
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
        var mangas = await mangaRepository.GetByIdsAsync(recList, ct);
        return mangas;
    }

    public async Task<List<Manga>> GetSimilarAsync(Guid mangaId, int limit, CancellationToken ct = default)
    {
        var similarIds = await qdrantService.SearchSimilarAsync(mangaId, limit, ct);
        if (similarIds.Count == 0) return new List<Manga>();
        var mangas = await mangaRepository.GetByIdsAsync(similarIds, ct);
        return mangas;
    }

    public async Task<List<Manga>> SemanticSearchAsync(string query, int limit, CancellationToken ct = default)
    {
        var ids = await qdrantService.SemanticSearchAsync(query, limit, ct);
        if (ids.Count == 0) return new List<Manga>();
        return await mangaRepository.GetByIdsAsync(ids, ct);
    }

    public async Task<List<Manga>> GetSimilarFilteredAsync(
        Guid mangaId,
        string? status,
        string? type,
        List<string>? genres,
        int limit,
        CancellationToken ct = default)
    {
        var ids = await qdrantService.SearchSimilarFilteredAsync(mangaId, status, type, genres, limit, ct);
        if (ids.Count == 0) return new List<Manga>();
        return await mangaRepository.GetByIdsAsync(ids, ct);
    }

    public async Task<List<Manga>> GetAdvancedRecommendationAsync(
        List<Guid> likedIds,
        List<Guid> dislikedIds,
        int limit,
        CancellationToken ct = default)
    {
        var ids = await qdrantService.RecommendAdvancedAsync(likedIds, dislikedIds, limit, ct);
        if (ids.Count == 0) return new List<Manga>();
        return await mangaRepository.GetByIdsAsync(ids, ct);
    }

    private static Manga MapToDomain(MeiliMangaDocument doc)
    {
        return Manga.Reconstitute(
            MangaId.From(Guid.Parse(doc.Id)),
            doc.Title,
            doc.Author,
            doc.Type,
            doc.MalId,
            doc.AnilistId,
            doc.MangaUpdateId,
            doc.Genres,
            doc.Categories,
            doc.Description,
            doc.ImageUrl,
            doc.LocalImageUrl,
            0,
            doc.Rating,
            doc.Popularity,
            doc.Members,
            doc.Nsfw,
            doc.Status,
            DateTimeOffset.FromUnixTimeSeconds(doc.ReleaseDate).UtcDateTime,
            doc.TotalView,
            DateTimeOffset.FromUnixTimeSeconds(doc.CreatedAtTimestamp).UtcDateTime,
            DateTimeOffset.FromUnixTimeSeconds(doc.UpdatedAtTimestamp).UtcDateTime,
            doc.Url,
            doc.LatestChapter.Select(c=>new Chapter(
                ChapterId.From(Guid.Parse(c.Id)),
                c.Number,
                c.Link,
                c.ChapterProvider,
                c.ChapterProviderIcon,
                c.Language,
                c.TotalView,
                DateTimeOffset.FromUnixTimeSeconds(c.UploadDateTimestamp).UtcDateTime,
                null
                )).ToList());
    }

    public async Task IndexMangaAsync(Manga manga, CancellationToken ct = default)
    {
        await meilisearchService.IndexMangaAsync(manga, ct);
    }
}