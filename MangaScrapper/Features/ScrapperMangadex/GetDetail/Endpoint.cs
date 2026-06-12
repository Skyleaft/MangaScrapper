using FastEndpoints;
using MangaScrapper.Features.ScrapperMangadex.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperMangadex.GetDetail;

public class Endpoint(MangaDexService mangaDexService) : Endpoint<DetailRequest, MangaDocument>
{
    public override void Configure()
    {
        Get("/api/scrapper/mangadex/manga/detail");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DetailRequest r, CancellationToken ct)
    {
        var data = await mangaDexService.GetDetail(r.MangaUrl, ct);
        await Send.OkAsync(data, ct);
    }
}
