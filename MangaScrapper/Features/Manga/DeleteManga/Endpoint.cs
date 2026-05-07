using FastEndpoints;
using Hangfire;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.DeleteManga;

public class Endpoint(IMangaRepository mangaRepository, IBackgroundJobClient jobClient) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/api/manga/{MangaId}");
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(r.MangaId, ct);
        if (manga == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        jobClient.Enqueue<DeleteMangaJob>(job => job.ExecuteAsync(manga.Id, manga.Title, CancellationToken.None));
        
        await Send.OkAsync(cancellation: ct);
    }
}
