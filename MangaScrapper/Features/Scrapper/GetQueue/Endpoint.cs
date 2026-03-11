using FastEndpoints;
using Hangfire;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.GetQueue;

public record JobQueueItem(
    string Id,
    string JobName,
    string State,
    DateTime CreatedAt
);

public class Endpoint(IMangaRepository repository, KomikuService komikuService, ILogger<Endpoint> logger) : EndpointWithoutRequest<List<JobQueueItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/queue");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var items = new List<JobQueueItem>();

        // Get enqueued jobs (queued for processing)
        var enqueuedJobs = monitoringApi.EnqueuedJobs("default", 0, int.MaxValue);
        foreach (var job in enqueuedJobs)
        {
            items.Add(new JobQueueItem(
                job.Key,
                job.Value.Job?.Method.Name ?? "Unknown",
                "Enqueued",
                DateTime.UtcNow
            ));
        }

        // Get fetched jobs (currently being processed)
        var fetchedJobs = monitoringApi.FetchedJobs("default", 0, int.MaxValue);
        foreach (var job in fetchedJobs)
        {
            items.Add(new JobQueueItem(
                job.Key,
                job.Value.Job?.Method.Name ?? "Unknown",
                "Processing",
                DateTime.UtcNow
            ));
        }

        await Send.OkAsync(items, ct);
    }
}