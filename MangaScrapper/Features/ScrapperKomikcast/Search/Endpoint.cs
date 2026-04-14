using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Features.ScrapperKomikcast.Services;
using MangaScrapper.Infrastructure.Models;

namespace MangaScrapper.Features.ScrapperKomikcast.Search;

public class Endpoint(KomikcastService komikcastService) : Endpoint<SearchRequest, List<SearchItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/komikcast/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SearchRequest r, CancellationToken ct)
    {
        var data = await komikcastService.SearchManga(r, ct);
        await Send.OkAsync(data, ct);
    }
}
