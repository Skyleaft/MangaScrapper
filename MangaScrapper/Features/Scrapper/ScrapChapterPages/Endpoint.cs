using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.ScrapChapterPages;

public class Endpoint(KomikuService komikuService,KiryuuService kiryuuService, IMangaRepository mangaRepository) : Endpoint<Request>
{
    public override void Configure()
    {
        Get("/api/scrapper/manga/{MangaId}/chapter-pages");
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
            if (chapter.ChapterProvider == "Komiku" && chapter.Pages.Count ==0)
            {
                await komikuService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
            else if (chapter.ChapterProvider == "Kiryuu" && chapter.Pages.Count ==0)
            {
                await kiryuuService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
        }

        await Send.OkAsync(new { Message = "Scraping jobs queued for missing chapters." }, ct);
    }
}
