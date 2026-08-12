using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.MangaData.GetPagedManga;

public sealed class GetPagedMangaQueryHandler(IMangaRepository mangaRepository, IMangaSearchRepository mangaSearchRepository)
    : IQueryHandler<GetPagedMangaQuery, PagedResponse<MangaSummaryResponse>>
{
    public async Task<Result<PagedResponse<MangaSummaryResponse>>> Handle(
        GetPagedMangaQuery query,
        CancellationToken ct)
    {
        var data = await mangaSearchRepository.SearchAsync(query.Search,
            query.Genres,
            query.Status,
            query.Type,
            query.SortBy,
            query.OrderBy,
            query.Page,
            query.PageSize,
            ct);
        
        // Collect the IDs from Meilisearch results to fetch full documents from MongoDB
        var ids = data.Items.Select(x => x.Id.Value).ToList();
        
        // Fetch full manga documents from MongoDB (with chapter data for LatestChapter)
        var mongoDocs = new Dictionary<Guid, Domain.Aggregates.Manga>();
        if (ids.Count > 0)
        {
            var fullDocs = await mangaRepository.GetByIdsAsync(ids, ct);
            mongoDocs = fullDocs.ToDictionary(m => m.Id.Value);
        }
        
        var mapped = data.Items.Select(m =>
        {
            var id = m.Id.Value;
            mongoDocs.TryGetValue(id, out var mongoDoc);

            
            var latest = mongoDoc?.Chapters.OrderByDescending(c => c.Number).FirstOrDefault();
            var latestSummary = latest is null
                ? new ChapterResponse(Guid.Empty, 0, "",new List<string>(),"", null, null, DateTime.MinValue,0)
                : new ChapterResponse(latest.Id.Value, latest.Number, latest.Link, latest.Pages.Select(x=>x.LocalImageUrl).ToList(), latest.Language, latest.ChapterProvider, latest.ChapterProviderIcon,latest.UploadDate,latest.TotalView);

            return new MangaSummaryResponse(
                id,
                mongoDoc.MalId,
                mongoDoc.AnilistId ?? 0,
                m.Title,
                m.Author,
                m.Type,
                m.Genres,
                m.Description,
                m.ImageUrl,
                m.LocalImageUrl,
                m.ThumbnailSize,
                m.Rating,
                m.Popularity,
                mongoDoc.Members,
                m.ReleaseDate,
                m.Status,
                m.CreatedAt,
                m.UpdatedAt,
                mongoDoc.Url,
                m.TotalView > 0 ? m.TotalView : mongoDoc?.Chapters.Sum(c => c.TotalView) ?? 0,
                latestSummary
                );
            
        });

        return PagedResponse<MangaSummaryResponse>.Create(
            mapped,
            data.Page,
            data.PageSize,
            data.TotalCount);
    }
}
