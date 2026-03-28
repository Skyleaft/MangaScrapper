using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Shared.Models;

namespace MangaScrapper.Features.Scrapper.SearchJikan;

public class Endpoint(KomikuService komikuService) : Endpoint<Request, List<JikanMangaItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/jikan/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var results = await komikuService.SearchJikan(r.Title, ct);
        await Send.OkAsync(results, ct);
    }
}
