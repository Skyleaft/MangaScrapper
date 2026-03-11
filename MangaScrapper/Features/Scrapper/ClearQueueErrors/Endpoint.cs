using FastEndpoints;
using Hangfire;

namespace MangaScrapper.Features.Scrapper.ClearQueueErrors;

public class Endpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/scrapper/queue/errors");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var failedJobs = monitoringApi.FailedJobs(0, int.MaxValue);
        
        // In a production environment, you would delete these jobs
        // For now, we can use the IBackgroundJobClient to delete them
        var jobClient = new BackgroundJobClient(JobStorage.Current);
        foreach (var job in failedJobs)
        {
            jobClient.Delete(job.Key);
        }
        
        await Send.NoContentAsync(ct);
    }
}
