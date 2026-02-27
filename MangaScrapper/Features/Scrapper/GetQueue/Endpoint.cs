using FastEndpoints;
using MangaScrapper.Infrastructure.BackgroundJobs;

namespace MangaScrapper.Features.Scrapper.GetQueue;

public class Endpoint(IBackgroundTaskQueue taskQueue) : EndpointWithoutRequest<List<QueueItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/queue");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var items = taskQueue.GetQueueItems();
        await Send.OkAsync(items, ct);
    }
}