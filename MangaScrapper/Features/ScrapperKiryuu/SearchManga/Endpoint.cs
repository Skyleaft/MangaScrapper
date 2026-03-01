using FastEndpoints;
using MangaScrapper.Features.ScrapperKiryuu.Services;

namespace MangaScrapper.Features.ScrapperKiryuu.SearchManga;

public class Endpoint(KiryuuService kiryuuService) : Endpoint<Request, List<KiryuuManga>>
{
    public override void Configure()
    {
        Post("/api/scrapper/kiryuu/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var data = await kiryuuService.SearchManga(r.Keyword);
        await Send.OkAsync(data, ct);
    }
}
