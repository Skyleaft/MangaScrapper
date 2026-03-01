using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;

namespace MangaScrapper.Features.ScrapperKiryuu.GetSingleChapterPages;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, List<KiryuuPage>>
{
    public override void Configure()
    {
        Post("/api/scrapper/kiryuu/manga/pages");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var pages = await kiryuuService.GetAllPages(r.ChapterUrl);
        await Send.OkAsync(pages, ct);
    }
}
