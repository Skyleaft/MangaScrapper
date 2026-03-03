using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKiryuu.ScrapManga;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, MangaDocument>
{
    public override void Configure()
    {
        Post("/api/scrapper/kiryuu/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var data = await kiryuuService.ExtractManga(r.MangaUrl, ct, r.ScrapChapterPages);
        if (data == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(data, cancellation: ct);
    }
}
