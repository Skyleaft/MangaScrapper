using System.ComponentModel;
using Hangfire;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Infrastructure.BackgroundJobs;

public class DeleteMangaJob(
    IServiceProvider serviceProvider,
    ILogger<DeleteMangaJob> logger,
    IOptions<ScrapperSettings> settings)
{
    private readonly string _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath)
        ? settings.Value.ImageStoragePath
        : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);

    [Queue("default")]
    [DisplayName("Deleting Manga: {1}")]
    public async Task ExecuteAsync(Guid mangaId, string mangaTitle, CancellationToken ct)
    {
        logger.LogInformation("Starting deletion for manga: {MangaTitle} (ID: {MangaId})", mangaTitle, mangaId);

        using var scope = serviceProvider.CreateScope();
        var mangaRepository = scope.ServiceProvider.GetRequiredService<IMangaRepository>();
        var meilisearchService = scope.ServiceProvider.GetRequiredService<MeilisearchService>();
        var qdrantService = scope.ServiceProvider.GetRequiredService<QdrantService>();

        var manga = await mangaRepository.GetByIdAsync(mangaId, ct);
        if (manga == null)
        {
            logger.LogWarning("Manga with ID {MangaId} not found. It might have been deleted already.", mangaId);
            return;
        }

        var cleanTitle = GetCleanTitle(manga.Title);
        var mangaDir = Path.Combine(_imageStoragePath, cleanTitle);

        if (Directory.Exists(mangaDir))
        {
            try
            {
                Directory.Delete(mangaDir, true);
                logger.LogInformation("Deleted directory: {MangaDir}", mangaDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting directory: {MangaDir}", mangaDir);
            }
        }

        // Additional check for thumbnail if it's outside cleanTitle
        if (!string.IsNullOrEmpty(manga.LocalImageUrl))
        {
            var thumbnailPath = Path.Combine(_imageStoragePath, manga.LocalImageUrl);
            if (File.Exists(thumbnailPath))
            {
                try
                {
                    File.Delete(thumbnailPath);
                    logger.LogInformation("Deleted thumbnail: {ThumbnailPath}", thumbnailPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deleting thumbnail: {ThumbnailPath}", thumbnailPath);
                }
            }
        }

        await mangaRepository.DeleteAsync(mangaId, ct);
        await meilisearchService.DeleteMangaAsync(mangaId, ct);
        await qdrantService.DeleteMangaAsync(mangaId, ct);

        logger.LogInformation("Finished deletion for manga: {MangaTitle}", mangaTitle);
    }

    private static string GetCleanTitle(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Union(new[] { '?', '*', ':', '|', '<', '>', '"' })
            .ToArray();

        var cleaned = string.Concat(title.Split(invalidChars)).TrimEnd('.', ' ');
        return string.IsNullOrEmpty(cleaned) ? "manga" : cleaned;
    }
}
