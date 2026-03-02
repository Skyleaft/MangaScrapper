using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKiryuu.GetSingleChapterPages;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, List<PageDocument>>
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
