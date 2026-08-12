using MangaScrapper.Infrastructure.Scrapers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.Messaging;

namespace MangaScrapper.Infrastructure.Messaging;

/// <summary>
/// Handles <see cref="ScrapChapterPagesIntegrationEvent"/> messages received from RabbitMQ.
/// Resolves the correct scraper by provider key, fetches chapter pages, and persists them.
/// </summary>
public sealed class ScrapChapterPagesHandler(
    IServiceProvider serviceProvider,
    ILogger<ScrapChapterPagesHandler> logger)
    : IIntegrationEventHandler<ScrapChapterPagesIntegrationEvent>
{
    public async Task HandleAsync(ScrapChapterPagesIntegrationEvent evt, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Processing ScrapChapterPages event: Manga={MangaTitle}, Chapter={ChapterNumber}, Provider={Provider}",
            evt.MangaTitle, evt.ChapterNumber, evt.Provider);

        using var scope = serviceProvider.CreateScope();

        var scrapper = scope.ServiceProvider.GetKeyedService<IScrapperService>(evt.Provider)
            ?? throw new InvalidOperationException($"No IScrapperService registered for provider key '{evt.Provider}'.");

        var repo = scope.ServiceProvider.GetRequiredService<MangaScrapper.Domain.Repositories.IMangaRepository>();

        var manga = await repo.GetByIdAsync(MangaScrapper.Domain.ValueObjects.MangaId.From(evt.MangaId), ct)
            ?? throw new InvalidOperationException($"Manga with ID '{evt.MangaId}' not found.");

        var chapterId = Guid.Parse(evt.ChapterId);
        var chapter = manga.Chapters?.FirstOrDefault(c => c.Id.Value == chapterId);
        if (chapter is null)
            throw new InvalidOperationException($"Chapter '{evt.ChapterId}' not found in manga '{evt.MangaTitle}'.");

        var docChapter = new MangaScrapper.Infrastructure.Persistence.Documents.ChapterDocument
        {
            Id = chapter.Id.Value,
            Number = chapter.Number,
            Link = chapter.Link,
            ChapterProvider = chapter.ChapterProvider,
            ChapterProviderIcon = chapter.ChapterProviderIcon,
            Language = chapter.Language,
            TotalView = chapter.TotalView,
            UploadDate = chapter.UploadDate
        };

        var processedChapter = await scrapper.GetChapterPage(evt.MangaTitle, docChapter, ct);

        if (processedChapter.Pages is not { Count: > 0 })
        {
            logger.LogWarning(
                "No pages scraped for Manga={MangaTitle}, Chapter={ChapterNumber}. Skipping update.",
                evt.MangaTitle, evt.ChapterNumber);
            return;
        }

        var domainPages = processedChapter.Pages.Select(p => new MangaScrapper.Domain.Aggregates.Page(Guid.NewGuid(), p.ImageUrl, p.LocalImageUrl, p.Size)).ToList();
        await repo.UpdateChapterPagesAsync(evt.MangaId, chapterId, domainPages, ct);

        logger.LogInformation(
            "Finished ScrapChapterPages: Manga={MangaTitle}, Chapter={ChapterNumber}, Pages={PageCount}",
            evt.MangaTitle, evt.ChapterNumber, processedChapter.Pages.Count);
    }
}
