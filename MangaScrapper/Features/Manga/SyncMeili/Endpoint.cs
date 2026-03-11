using FastEndpoints;
using Hangfire;
using MangaScrapper.Infrastructure.BackgroundJobs;

namespace MangaScrapper.Features.Manga.SyncMeili;

public class Response
{
    public string Message { get; set; } = string.Empty;
}

public class Endpoint : EndpointWithoutRequest<Response>
{
    public override void Configure()
    {
        Post("/api/manga/sync-meili");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        BackgroundJob.Enqueue<MeiliSyncJob>(job => job.ExecuteAsync(CancellationToken.None));

        await Send.OkAsync(new Response { Message = "Meilisearch sync job has been queued." }, cancellation: ct);
    }
}
