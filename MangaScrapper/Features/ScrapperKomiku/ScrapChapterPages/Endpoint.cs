using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.ScrapperKomiku.ScrapChapterPages;

public class Endpoint(KomikuService komikuService, IMangaRepository mangaRepository) : Endpoint<Request>
{
    public override void Configure()
    {
        Get("/api/scrapper/komiku/manga/{MangaId}/chapter-pages");
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

        foreach (var chapter in manga.Chapters)
        {
            if (chapter.Pages.Count == 0)
            {
                await komikuService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
        }

        await Send.OkAsync(new { Message = "Scraping jobs queued for missing chapters." }, ct);
    }
}
