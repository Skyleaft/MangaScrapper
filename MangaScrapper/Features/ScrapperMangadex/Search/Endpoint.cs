using FastEndpoints;
using MangaScrapper.Features.ScrapperMangadex.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.ScrapperMangadex.Search;

public class Endpoint(MangaDexService mangadexService, IMangaRepository mangaRepository) : Endpoint<SearchRequest, List<SearchItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/mangadex/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SearchRequest r, CancellationToken ct)
    {
        var data = await mangadexService.SearchManga(r, ct);
        await Send.OkAsync(data, ct);
    }
}
