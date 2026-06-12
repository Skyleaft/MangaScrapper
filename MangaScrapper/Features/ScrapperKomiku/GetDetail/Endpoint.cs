using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.ScrapperKomiku.GetDetail;

public class Endpoint(KomikuService komikuService) : Endpoint<DetailRequest, MangaDocument>
{
    public override void Configure()
    {
        Get("/api/scrapper/komiku/manga/detail");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DetailRequest r, CancellationToken ct)
    {
        var data = await komikuService.GetDetail(r.MangaUrl, ct);
        await Send.OkAsync(data, ct);
    }
}
