using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using MangaScrapper.Infrastructure.Models;

namespace MangaScrapper.Features.ScrapperKiryuu.SearchManga;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<SearchRequest, List<SearchItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/kiryuu/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SearchRequest r, CancellationToken ct)
    {
        var data = await kiryuuService.SearchManga(r, ct);
        await Send.OkAsync(data, ct);
    }
}
