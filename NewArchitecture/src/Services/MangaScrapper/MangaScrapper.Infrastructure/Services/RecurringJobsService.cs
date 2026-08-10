using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Storage;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Application.Features.RecurringJobs;
using MangaScrapper.Infrastructure.BackgroundJobs;
using RecurringJobDto = MangaScrapper.Application.Features.RecurringJobs.RecurringJobDto;

namespace MangaScrapper.Infrastructure.Services;

public class RecurringJobsService(IRecurringJobManager recurringJobManager) : IRecurringJobsService
{
    public Task<List<RecurringJobDto>> GetRecurringJobsAsync(CancellationToken ct = default)
    {
        var connection = JobStorage.Current.GetConnection();
        var jobs = connection.GetRecurringJobs()
            .Select(j => new RecurringJobDto(
                j.Id,
                j.Cron,
                j.Queue,
                j.NextExecution,
                j.LastExecution,
                j.LastJobState,
                j.CreatedAt
            )).ToList();

        return Task.FromResult(jobs);
    }

    public Task CreateOrUpdateLatestChapterScrapingJobAsync(string jobId, string cronExpression, string provider, int scrapLastTotalPage, CancellationToken ct = default)
    {
        recurringJobManager.AddOrUpdate<LatestChapterScrapingJob>(
            jobId,
            job => job.ExecuteAsync(scrapLastTotalPage, provider, CancellationToken.None),
            cronExpression
        );
        return Task.CompletedTask;
    }

    public Task DeleteRecurringJobAsync(string jobId, CancellationToken ct = default)
    {
        recurringJobManager.RemoveIfExists(jobId);
        return Task.CompletedTask;
    }

    public Task TriggerRecurringJobAsync(string jobId, CancellationToken ct = default)
    {
        recurringJobManager.Trigger(jobId);
        return Task.CompletedTask;
    }
}
