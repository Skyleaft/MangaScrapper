using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.ScrapperKomiku.ScrapManga;

public class Endpoint(ScrapperService scrapperService, IMangaRepository mangaRepository) : Endpoint<Request,MangaDocument>
{
    public override void Configure()
    {
        Post("/api/scrapper/komiku/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r,CancellationToken ct)
    {
        var manga = await scrapperService.ExtractMangaMetadata(r.MangaUrl, ct, r.ScrapChapters);
        
        await Send.OkAsync(manga, ct);
    }
}