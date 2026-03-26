using FastEndpoints;
using Hangfire;

namespace MangaScrapper.Features.RecurringJobs.Delete;

public class Endpoint(IRecurringJobManager recurringJobManager) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Delete("/api/recurring-jobs/{JobId}");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        recurringJobManager.RemoveIfExists(req.JobId);
        await Send.OkAsync(new Response { Message = "Job deleted successfully" }, ct);
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
