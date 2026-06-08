using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.GetTrending;

public class Endpoint(IMangaRepository mangaRepository) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/manga/trending");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var (mongoDocs, totalCount) = await mangaRepository.GetTrendingAsync(
            r.Search,
            r.Genres,
            r.Status,
            r.Type,
            r.Page,
            r.PageSize,
            ct);

        var response = new Response
        {
            Items = mongoDocs.Select(mongoDoc => new MangaSummary
            {
                Id = mongoDoc.Id,
                Title = mongoDoc.Title,
                Author = mongoDoc.Author,
                Type = mongoDoc.Type,
                Genres = mongoDoc.Genres,
                Description = mongoDoc.Description,
                ImageUrl = mongoDoc.ImageUrl,
                LocalImageUrl = mongoDoc.LocalImageUrl,
                Status = mongoDoc.Status,
                CreatedAt = mongoDoc.CreatedAt,
                UpdatedAt = mongoDoc.UpdatedAt,
                Url = mongoDoc.Url,
                MalId = mongoDoc.MalID,
                Members = mongoDoc.Members,
                Popularity = mongoDoc.Popularity,
                Rating = mongoDoc.Rating,
                ReleaseDate = mongoDoc.ReleaseDate,
                TotalView = mongoDoc.TotalView > 0 ? mongoDoc.TotalView : mongoDoc.Chapters?.Sum(c => c.TotalView) ?? 0,
                LatestChapter = mongoDoc.Chapters?
                    .OrderByDescending(c => c.Number)
                    .Select(c => new LatestChapterSummary
                    {
                        Id = c.Id,
                        Number = c.Number,
                        TotalView = c.TotalView,
                        UploadDate = c.UploadDate,
                        ChapterProvider = c.ChapterProvider,
                        ChapterProviderIcon = c.ChapterProviderIcon
                    }).FirstOrDefault() ?? new()
            }).ToList(),
            TotalCount = totalCount,
            Page = r.Page,
            PageSize = r.PageSize
        };

        await Send.OkAsync(response, ct);
    }
}
