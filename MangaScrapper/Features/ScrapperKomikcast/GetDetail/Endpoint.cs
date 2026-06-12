using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.ScrapperKomikcast.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKomikcast.GetDetail;

public class Endpoint(KomikcastService komikcastService) : Endpoint<DetailRequest, MangaDocument>
{
    public override void Configure()
    {
        Get("/api/scrapper/komikcast/manga/detail");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DetailRequest r, CancellationToken ct)
    {
        var data = await komikcastService.GetDetail(r.MangaUrl, ct);
        await Send.OkAsync(data, ct);
    }
}
