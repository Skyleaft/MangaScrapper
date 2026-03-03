using FastEndpoints;
using MangaScrapper.Infrastructure.BackgroundJobs;

namespace MangaScrapper.Features.Scrapper.ClearQueueErrors;

public class Endpoint(IBackgroundTaskQueue taskQueue) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/scrapper/queue/errors");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        taskQueue.ClearErrorItems();
        await Send.NoContentAsync(ct);
    }
}
