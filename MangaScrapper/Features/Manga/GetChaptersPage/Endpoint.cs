using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Manga.GetChaptersPage;

public class Endpoint(ScrapperService scrapperService, IMangaRepository mangaRepository) : Endpoint<Request>
{
    public override void Configure()
    {
        Get("/api/manga/{MangaId}/chapter/{Chapter}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r,CancellationToken ct)
    {
        

        var manga = await mangaRepository.GetByIdAsync(r.MangaId,ct);
        var url = manga.Url;
        var doc = await scrapperService.GetHtml(url);
        
        await Send.OkAsync("asd",ct);
    }
}