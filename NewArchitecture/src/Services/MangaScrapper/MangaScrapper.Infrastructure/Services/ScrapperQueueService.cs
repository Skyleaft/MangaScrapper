using Hangfire;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Infrastructure.Persistence.Documents;
using MangaScrapper.Infrastructure.Scrapers;
using Microsoft.Extensions.DependencyInjection;

namespace MangaScrapper.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation that routes chapter scraping to the correct provider
/// and enqueues Hangfire jobs. Bridges the domain <see cref="Chapter"/> to the
/// infrastructure <see cref="ChapterDocument"/> expected by <see cref="IScrapperService"/>.
/// </summary>
public sealed class ScrapperQueueService : IScrapperQueueService
{
    private readonly IServiceProvider _serviceProvider;

    public ScrapperQueueService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task QueueChapterScraping(Guid mangaId, string mangaTitle, Chapter chapter)
    {
        var providerKey = chapter.ChapterProvider?.ToLowerInvariant() switch
        {
            "komiku"    => "komiku",
            "kiryuu"    => "kiryuu",
            "komikcast" => "komikcast",
            "mangadex"  => "mangadex",
            _           => null
        };

        if (providerKey is null) return;

        var service = _serviceProvider.GetKeyedService<IScrapperService>(providerKey);
        if (service is null) return;

        // Map domain Chapter -> infrastructure ChapterDocument
        var chapterDoc = new ChapterDocument
        {
            Id              = chapter.Id.Value,
            Number          = chapter.Number,
            Link            = chapter.Link,
            ChapterProvider = chapter.ChapterProvider,
            ChapterProviderIcon = chapter.ChapterProviderIcon,
            Language        = chapter.Language,
            TotalView       = chapter.TotalView,
            UploadDate      = chapter.UploadDate
        };

        await service.QueueChapterScraping(mangaId, mangaTitle, chapterDoc);
    }

    public Task<List<(string Id, string JobName, string State)>> GetQueuedJobsAsync()
    {
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var items = new List<(string, string, string)>();

        foreach (var job in monitoringApi.EnqueuedJobs("default", 0, int.MaxValue))
            items.Add((job.Key, job.Value.Job?.Method.Name ?? "Unknown", "Enqueued"));

        foreach (var job in monitoringApi.FetchedJobs("default", 0, int.MaxValue))
            items.Add((job.Key, job.Value.Job?.Method.Name ?? "Unknown", "Processing"));

        return Task.FromResult(items);
    }
}
