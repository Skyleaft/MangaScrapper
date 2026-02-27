using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.GetManga;

public class Endpoint(IMangaRepository mangaRepository) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/manga/{MangaId}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(r.MangaId, ct);

        if (manga == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = new Response
        {
            Id = manga.Id,
            Title = manga.Title,
            Author = manga.Author,
            Type = manga.Type,
            Genres = manga.Genres,
            Description = manga.Description,
            ImageUrl = manga.ImageUrl,
            LocalImageUrl = manga.LocalImageUrl,
            Status = manga.Status,
            CreatedAt = manga.CreatedAt,
            UpdatedAt = manga.UpdatedAt,
            Url = manga.Url,
            Chapters = manga.Chapters.Select(c => new ChapterResponse
            {
                Id = c.Id,
                Number = c.Number,
                Link = c.Link,
                TotalView = c.TotalView,
                UploadDate = c.UploadDate
            }).ToList()
        };

        await Send.OkAsync(response, ct);
    }
}