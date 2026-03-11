using System.ComponentModel;
using Hangfire;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MangaScrapper.Infrastructure.BackgroundJobs;

public class ChapterScrapingJob(
    IServiceProvider serviceProvider,
    ILogger<ChapterScrapingJob> logger)
{
    [Queue("default")]
    [DisplayName("Scraping {1} - Chapter {2}")]
    public async Task ExecuteAsync(Guid mangaId, string mangaTitle, double chapterNumber, string chapterId, string serviceType, CancellationToken ct)
    {
        logger.LogInformation("Starting scraping for {MangaTitle} - Chapter {ChapterNumber}", mangaTitle, chapterNumber);

        using var scope = serviceProvider.CreateScope();
        
        var type = Type.GetType(serviceType);
        if (type == null)
        {
            throw new Exception($"Service type {serviceType} not found.");
        }
        
        var scopedScrapper = (ScrapperServiceBase)scope.ServiceProvider.GetRequiredService(type);
        var scopedRepo = scope.ServiceProvider.GetRequiredService<IMangaRepository>();

        var manga = await scopedRepo.GetByIdAsync(mangaId, ct);
        if (manga == null)
        {
            throw new Exception($"Manga with ID {mangaId} not found.");
        }

        var guidChapterId = Guid.Parse(chapterId);
        var chapter = manga.Chapters.FirstOrDefault(c => c.Id == guidChapterId);
        if (chapter == null)
        {
            throw new Exception($"Chapter with ID {chapterId} not found in manga {mangaTitle}.");
        }

        var processedChapter = await scopedScrapper.GetChapterPage(mangaTitle, chapter, ct);
        await scopedRepo.UpdateChapterPagesAsync(mangaId, guidChapterId, processedChapter.Pages, ct);
        
        logger.LogInformation("Finished scraping for {MangaTitle} - Chapter {ChapterNumber}", mangaTitle, chapterNumber);
    }
}
