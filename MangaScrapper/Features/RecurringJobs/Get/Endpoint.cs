using FastEndpoints;
using Hangfire;
using Hangfire.Storage;

namespace MangaScrapper.Features.RecurringJobs.Get;

public class Endpoint : EndpointWithoutRequest<List<RecurringJobDto>>
{
    public override void Configure()
    {
        Get("/api/recurring-jobs");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var connection = JobStorage.Current.GetConnection();
        var jobs = connection.GetRecurringJobs()
            .Select(j => new RecurringJobDto
            {
                Id = j.Id,
                Cron = j.Cron,
                Queue = j.Queue,
                NextExecution = j.NextExecution,
                LastExecution = j.LastExecution,
                LastJobState = j.LastJobState,
                CreatedAt = j.CreatedAt
            }).ToList();

        await Send.OkAsync(jobs, ct);
    }
}

public class RecurringJobDto
{
    public string Id { get; set; } = string.Empty;
    public string Cron { get; set; } = string.Empty;
    public string Queue { get; set; } = string.Empty;
    public DateTime? NextExecution { get; set; }
    public DateTime? LastExecution { get; set; }
    public string LastJobState { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}
