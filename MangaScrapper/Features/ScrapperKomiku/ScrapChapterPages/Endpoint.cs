using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.ScrapperKomiku.ScrapChapterPages;

public class Endpoint(ScrapperService scrapperService, IMangaRepository mangaRepository) : Endpoint<Request>
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
            if (chapter.Pages == null || chapter.Pages.Count == 0)
            {
                await scrapperService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
        }

        await Send.OkAsync(new { Message = "Scraping jobs queued for missing chapters." }, ct);
    }
}