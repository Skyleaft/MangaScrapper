using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;

namespace MangaScrapper.Features.ScrapperKiryuu.GetAllChapters;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, List<KiryuuChapter>>
{
    public override void Configure()
    {
        Get("/api/scrapper/kiryuu/manga/{MangaId}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var chapters = await kiryuuService.GetAllChapter(r.MangaId);
        await Send.OkAsync(chapters, ct);
    }
}
