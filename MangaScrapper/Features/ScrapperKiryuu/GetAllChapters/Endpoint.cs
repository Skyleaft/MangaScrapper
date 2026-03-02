using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKiryuu.GetAllChapters;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, List<ChapterDocument>>
{
    public override void Configure()
    {
        Get("/api/scrapper/kiryuu/manga/{MangaId}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var chapters = await kiryuuService.GetAllChaptersById(r.MangaId);
        await Send.OkAsync(chapters, ct);
    }
}
