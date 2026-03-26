using FastEndpoints;
using Hangfire;
using MangaScrapper.Infrastructure.BackgroundJobs;

namespace MangaScrapper.Features.RecurringJobs.CreateOrUpdate;

public class Endpoint(IRecurringJobManager recurringJobManager) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/recurring-jobs");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        // Add or update LatestChapterScrapingJob
        recurringJobManager.AddOrUpdate<LatestChapterScrapingJob>(
            req.JobId,
            job => job.ExecuteAsync(req.ScrapLastTotalPage, req.Provider, CancellationToken.None),
            req.CronExpression
        );

        await Send.OkAsync(new Response { Message = "Job created or updated successfully" }, ct);
    }
}

public class Request
{
    public string JobId { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int ScrapLastTotalPage { get; set; } = 1;
}

public class Response
{
    public string Message { get; set; } = string.Empty;
}
