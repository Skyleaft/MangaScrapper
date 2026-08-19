using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Persistence.Documents;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.Scrapers;
using MangaScrapper.Core.ValueObjects;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.Messaging;

namespace MangaScrapper.Core.Messaging;

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
        try
        {
            logger.LogInformation(
                "Processing ScrapChapterPages event: Manga={MangaTitle}, Chapter={ChapterNumber}, Provider={Provider}",
                evt.MangaTitle, evt.ChapterNumber, evt.Provider);

            using var scope = serviceProvider.CreateScope();

            var scrapper = scope.ServiceProvider.GetKeyedService<IScrapperService>(evt.Provider)
                ?? throw new InvalidOperationException($"No IScrapperService registered for provider key '{evt.Provider}'.");

            var repo = scope.ServiceProvider.GetRequiredService<IMangaRepository>();

            var manga = await repo.GetByIdAsync(MangaId.From(evt.MangaId), ct)
                ?? throw new InvalidOperationException($"Manga with ID '{evt.MangaId}' not found.");

            var chapterId = Guid.Parse(evt.ChapterId);
            var chapter = manga.Chapters?.FirstOrDefault(c => c.Id.Value == chapterId);
            if (chapter is null)
                throw new InvalidOperationException($"Chapter '{evt.ChapterId}' not found in manga '{evt.MangaTitle}'.");

            var processedChapter = await scrapper.GetChapterPage(evt.MangaTitle, chapter, ct);

            if (processedChapter.Pages is not { Count: > 0 })
            {
                logger.LogWarning(
                    "No pages scraped for Manga={MangaTitle}, Chapter={ChapterNumber}. Skipping update.",
                    evt.MangaTitle, evt.ChapterNumber);
                return;
            }

            await repo.UpdateChapterPagesAsync(evt.MangaId, chapterId, processedChapter.Pages, ct);

            var eventBus = scope.ServiceProvider.GetService<IEventBus>();
            if (eventBus != null)
            {
                var scrapedEvent = new ChapterPagesScrapedIntegrationEvent(
                    evt.MangaId,
                    evt.MangaTitle,
                    chapterId,
                    evt.ChapterNumber,
                    processedChapter.Pages.Count);

                await eventBus.PublishAsync(scrapedEvent, "chapter-pages-scraped", ct);
                logger.LogInformation(
                    "Published ChapterPagesScrapedIntegrationEvent for Manga={MangaTitle}, Chapter={ChapterNumber}",
                    evt.MangaTitle, evt.ChapterNumber);
            }

            logger.LogInformation(
                "Finished ScrapChapterPages: Manga={MangaTitle}, Chapter={ChapterNumber}, Pages={PageCount}",
                evt.MangaTitle, evt.ChapterNumber, processedChapter.Pages.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process ScrapChapterPages event for Manga={MangaTitle}, Chapter={ChapterNumber}. The event was dropped to prevent infinite retry loops.", evt.MangaTitle, evt.ChapterNumber);
        }
    }
}
