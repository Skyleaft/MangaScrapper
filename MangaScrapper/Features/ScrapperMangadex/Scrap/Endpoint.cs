using FastEndpoints;
using MangaScrapper.Features.ScrapperMangadex.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperMangadex.Scrap;

public class Endpoint(MangaDexService mangaDexService) : Endpoint<Request, MangaDocument>
{
    public override void Configure()
    {
        Post("/api/scrapper/mangadex/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var data = await mangaDexService.ExtractManga(r.MangaUrl, ct, r.ScrapChapterPages, r.LinkId);
        if (data == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(data, cancellation: ct);
    }
}
