using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;

namespace MangaScrapper.Features.ScrapperKiryuu.GetManga;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, KiryuuManga>
{
    public override void Configure()
    {
        Post("/api/scrapper/kiryuu/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var data = await kiryuuService.GetManga(r.Url);
        if (data == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (r.ScraptChapterPages && data.Chapters != null)
        {
            var chapterPageTasks = data.Chapters.Select(async (chapter, index) =>
            {
                var pages = string.IsNullOrWhiteSpace(chapter.Url)
                    ? new List<KiryuuPage>()
                    : await kiryuuService.GetAllPages(chapter.Url);
                return (Index: index, Pages: pages);
            });

            var pagesByChapter = await Task.WhenAll(chapterPageTasks);

            foreach (var item in pagesByChapter.OrderBy(x => x.Index))
            {
                data.Chapters[item.Index].Pages = item.Pages;
            }
        }
        await Send.OkAsync(data, cancellation: ct);
    }
}
