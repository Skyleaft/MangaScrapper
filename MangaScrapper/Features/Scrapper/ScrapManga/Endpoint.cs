using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Scrapper.ScrapManga;

public class Endpoint(ScrapperService scrapperService, IMangaRepository mangaRepository) : Endpoint<Request,MangaDocument>
{
    public override void Configure()
    {
        Post("/api/scrapper/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r,CancellationToken ct)
    {
        var manga = await scrapperService.ExtractMangaMetadata(r.MangaUrl, ct);
        
        await Send.OkAsync(manga, ct);
    }
}