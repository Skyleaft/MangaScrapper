using MangaScrapper.Infrastructure.Configuration;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Infrastructure.Scrapers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.Messaging;

namespace MangaScrapper.Infrastructure.Messaging;

/// <summary>
/// Handles <see cref="DeleteMangaIntegrationEvent"/> messages received from RabbitMQ.
/// Deletes local files, removes from MongoDB, Meilisearch, and Qdrant.
/// </summary>
public sealed class DeleteMangaHandler(
    IServiceProvider serviceProvider,
    IOptions<ScrapperSettings> settings,
    ILogger<DeleteMangaHandler> logger)
    : IIntegrationEventHandler<DeleteMangaIntegrationEvent>
{
    private readonly string _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath)
        ? settings.Value.ImageStoragePath
        : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);

    public async Task HandleAsync(DeleteMangaIntegrationEvent evt, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Processing DeleteManga event: MangaId={MangaId}, Title={MangaTitle}",
            evt.MangaId, evt.MangaTitle);

        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMangaRepository>();
        var meilisearch = scope.ServiceProvider.GetRequiredService<Services.MeilisearchService>();
        var qdrant = scope.ServiceProvider.GetRequiredService<Services.QdrantService>();

        var manga = await repo.GetByIdAsync(Domain.ValueObjects.MangaId.From(evt.MangaId), ct);
        if (manga == null)
        {
            logger.LogWarning(
                "Manga with ID {MangaId} not found. It might have been deleted already.", evt.MangaId);
            return;
        }

        // Delete local chapter directory
        var cleanTitle = GetCleanTitle(manga.Title);
        var mangaDir = Path.Combine(_imageStoragePath, cleanTitle);

        if (Directory.Exists(mangaDir))
        {
            try
            {
                Directory.Delete(mangaDir, recursive: true);
                logger.LogInformation("Deleted manga directory: {MangaDir}", mangaDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting manga directory: {MangaDir}", mangaDir);
            }
        }

        // Delete thumbnail
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

        // Remove from all stores
        await repo.DeleteAsync(Domain.ValueObjects.MangaId.From(evt.MangaId), ct);
        await meilisearch.DeleteMangaAsync(evt.MangaId, ct);
        await qdrant.DeleteMangaAsync(evt.MangaId, ct);

        logger.LogInformation(
            "Finished DeleteManga event: MangaId={MangaId}, Title={MangaTitle}",
            evt.MangaId, evt.MangaTitle);
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
