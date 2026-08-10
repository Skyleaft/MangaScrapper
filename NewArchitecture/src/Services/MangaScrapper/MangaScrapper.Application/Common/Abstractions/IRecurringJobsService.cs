using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangaScrapper.Application.Features.RecurringJobs;

namespace MangaScrapper.Application.Common.Abstractions;

public interface IRecurringJobsService
{
    Task<List<RecurringJobDto>> GetRecurringJobsAsync(CancellationToken ct = default);
    Task CreateOrUpdateLatestChapterScrapingJobAsync(string jobId, string cronExpression, string provider, int scrapLastTotalPage, CancellationToken ct = default);
    Task DeleteRecurringJobAsync(string jobId, CancellationToken ct = default);
    Task TriggerRecurringJobAsync(string jobId, CancellationToken ct = default);
}
