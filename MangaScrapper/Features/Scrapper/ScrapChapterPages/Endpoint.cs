using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.ScrapperKomikcast.Services;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Features.ScrapperMangadex.Services;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.ScrapChapterPages;

public class Endpoint(KomikuService komikuService,KiryuuService kiryuuService,KomikcastService komikcastService,MangaDexService mangaDexService, IMangaRepository mangaRepository) : Endpoint<Request>
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

        var index = 0;
        foreach (var chapter in manga.Chapters.OrderBy(x=>x.Number))
        {
            if (chapter.ChapterProvider == "Komiku" && chapter.Pages.Count ==0)
            {
                await komikuService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
            else if (chapter.ChapterProvider == "Kiryuu" && chapter.Pages.Count ==0)
            {
                await kiryuuService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
            else if (chapter.ChapterProvider == "Komikcast" && chapter.Pages.Count ==0)
            {
                await komikcastService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
            else if (chapter.ChapterProvider == "MangaDex" && chapter.Pages.Count == 0)
            {
                await mangaDexService.QueueChapterScraping(manga.Id, manga.Title, chapter);
            }

            index++;
        }

        await Send.OkAsync(new { Message = $"Scraping {index} jobs queued for missing chapters." }, ct);
    }
}
