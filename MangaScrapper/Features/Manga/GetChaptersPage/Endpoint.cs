using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.GetChaptersPage;

public class Endpoint(IMangaRepository mangaRepository) : Endpoint<Request, List<PageDocument>>
{
    public override void Configure()
    {
        Get("/api/manga/{MangaId}/chapter/{Chapter}");
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

        var chapter = manga.Chapters.FirstOrDefault(c => Math.Abs(c.Number - r.Chapter) < 0.001);

        if (chapter == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(chapter.Pages, ct);
    }
}