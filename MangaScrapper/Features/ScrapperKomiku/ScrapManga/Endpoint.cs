using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKomiku.ScrapManga;

public class Endpoint(KomikuService komikuService) : Endpoint<Request, MangaDocument>
{
    public override void Configure()
    {
        Post("/api/scrapper/komiku/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var manga = await komikuService.ExtractMangaMetadata(r.MangaUrl, ct, r.ScrapChapters);
        await Send.OkAsync(manga, ct);
    }
}
