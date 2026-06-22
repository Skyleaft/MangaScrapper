using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.ScrapManga;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.ScrapperKomikcast.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKomikcast.Scrap;

public class Endpoint(KomikcastService komikcastService) : Endpoint<Request, MangaDocument>
{
    public override void Configure()
    {
        Post("/api/scrapper/komikcast/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var data = await komikcastService.ExtractManga(r.MangaUrl, ct, r.ScrapChapterPages,r.LinkId);
        if (data == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(data, cancellation: ct);
    }
}
