using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.UpdateMangaMetaData;

public class Endpoint(IBackgroundTaskQueue taskQueue, IMangaRepository repository, KomikuService komikuService, ILogger<Endpoint>logger) : Endpoint<Request>
{
    public override void Configure()
    {
        Get("/api/scrapper/manga/{MangaId}/metadata");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r,CancellationToken ct)
    {
        var manga = await repository.GetByIdAsync(r.MangaId, ct);
        if (manga == null)
        {
            await Send.NoContentAsync(ct);
        }
        manga = await komikuService.UpdateMangaDocument(manga);
        await repository.UpdateAsync(manga,ct);
        await Send.OkAsync($"Manga {manga.Title} Metadata Updated", cancellation: ct);
    }
}