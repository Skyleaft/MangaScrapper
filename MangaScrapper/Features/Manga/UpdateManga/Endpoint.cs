using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.UpdateManga;

public class Endpoint(IMangaRepository mangaRepository) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Put("/api/manga/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(r.Id, ct);

        if (manga == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        manga.MalID = r.MalId;
        manga.Author = r.Author;
        manga.Type = r.Type;
        manga.Genres = r.Genres;
        manga.Description = r.Description;
        manga.Rating = r.Rating;
        manga.ReleaseDate = r.ReleaseDate;
        manga.Status = r.Status;
        manga.TotalView = r.TotalView;
        manga.UpdatedAt = DateTime.UtcNow;

        await mangaRepository.UpdateAsync(manga, ct);

        await Send.OkAsync(new Response { Success = true, Message = "Manga updated successfully" }, ct);
    }
}
