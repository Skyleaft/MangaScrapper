using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.GetQueue;

public class Endpoint(IBackgroundTaskQueue taskQueue, IMangaRepository repository, KomikuService komikuService, ILogger<Endpoint>logger) : EndpointWithoutRequest<List<QueueItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/queue");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // var events = new ScraptMeta()
        // {
        //     KomikuService = komikuService,
        //     Repository = repository
        // };
        // await PublishAsync(events,Mode.WaitForNone, cancellation: ct);
        
        var items = taskQueue.GetQueueItems();
        await Send.OkAsync(items, ct);
    }
}