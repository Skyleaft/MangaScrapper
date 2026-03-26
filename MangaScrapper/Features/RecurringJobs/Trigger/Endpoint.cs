using FastEndpoints;
using Hangfire;

namespace MangaScrapper.Features.RecurringJobs.Trigger;

public class Endpoint(IRecurringJobManager recurringJobManager) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/recurring-jobs/{JobId}/trigger");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        recurringJobManager.Trigger(req.JobId);
        await Send.OkAsync(new Response { Message = "Job triggered successfully" }, ct);
    }
}

public class Request
{
    public string JobId { get; set; } = string.Empty;
}

public class Response
{
    public string Message { get; set; } = string.Empty;
}
