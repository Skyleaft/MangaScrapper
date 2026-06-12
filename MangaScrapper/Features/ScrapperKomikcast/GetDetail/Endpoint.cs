using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKiryuu.GetDetail;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<DetailRequest, MangaDocument>
{
    public override void Configure()
    {
        Get("/api/scrapper/kiryuu/manga/detail");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DetailRequest r, CancellationToken ct)
    {
        var data = await kiryuuService.GetDetail(r.MangaUrl, ct);
        await Send.OkAsync(data, ct);
    }
}
