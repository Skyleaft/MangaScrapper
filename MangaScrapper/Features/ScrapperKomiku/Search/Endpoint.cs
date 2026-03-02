using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.ScrapperKomiku.Search;

public class Endpoint(KomikuService komikuService, IMangaRepository mangaRepository) : Endpoint<SearchRequest, List<SearchItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/komiku/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SearchRequest r, CancellationToken ct)
    {
        var data = await komikuService.SearchPaged(r,ct);
        await Send.OkAsync(data, ct);
    }
}
