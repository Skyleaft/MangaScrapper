using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Manga.GetPagedManga;

internal sealed class GetPagedMangaQueryHandler(IMangaRepository mangaRepository)
    : IQueryHandler<GetPagedMangaQuery, PagedResponse<MangaSummaryResponse>>
{
    public async Task<Result<PagedResponse<MangaSummaryResponse>>> Handle(
        GetPagedMangaQuery query,
        CancellationToken ct)
    {
        var paged = await mangaRepository.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.Search,
            query.Type,
            query.Genre,
            ct);

        var mapped = paged.Items.Select(m =>
        {
            var latest = m.Chapters.OrderByDescending(c => c.Number).FirstOrDefault();
            var latestSummary = latest is null
                ? new LatestChapterSummaryResponse(Guid.Empty, 0, 0, null, null, string.Empty, DateTime.MinValue)
                : new LatestChapterSummaryResponse(latest.Id.Value, latest.Number, latest.TotalView, latest.ChapterProvider, latest.ChapterProviderIcon, latest.Language, latest.UploadDate);

            return new MangaSummaryResponse(
                m.Id.Value,
                m.MalId,
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
                m.Members,
                m.ReleaseDate,
                m.Status,
                m.CreatedAt,
                m.UpdatedAt,
                m.Url,
                m.TotalView,
                latestSummary);
        });

        return PagedResponse<MangaSummaryResponse>.Create(
            mapped,
            paged.Page,
            paged.PageSize,
            paged.TotalCount);
    }
}
