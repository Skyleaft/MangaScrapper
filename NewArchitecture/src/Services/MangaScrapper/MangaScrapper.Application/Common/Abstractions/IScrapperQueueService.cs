using MangaScrapper.Domain.Aggregates;

namespace MangaScrapper.Application.Common.Abstractions;

/// <summary>
/// Abstraction for queuing chapter-scraping background jobs and reading the job queue.
/// Implemented in Infrastructure so the Application layer stays free of Hangfire / provider deps.
/// </summary>
public interface IScrapperQueueService
{
    Task QueueChapterScraping(Guid mangaId, string mangaTitle, Chapter chapter);

    /// <summary>Returns enqueued + processing jobs as simple tuples (id, jobName, state).</summary>
    Task<List<(string Id, string JobName, string State)>> GetQueuedJobsAsync();
}
