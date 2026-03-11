using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Infrastructure.Services;

public class StorageSyncService
{
    private readonly IMangaRepository _mangaRepository;
    private readonly ILogger<StorageSyncService> _logger;
    private readonly string _imageStoragePath;

    public StorageSyncService(
        IMangaRepository mangaRepository,
        ILogger<StorageSyncService> logger,
        IOptions<ScrapperSettings> settings)
    {
        _mangaRepository = mangaRepository;
        _logger = logger;
        _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath)
            ? settings.Value.ImageStoragePath
            : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);
    }

    public async Task<StorageSyncReport> SyncStorageUsageAsync(CancellationToken ct = default)
    {
        var report = new StorageSyncReport();
        var mangas = await _mangaRepository.GetAllAsync(ct);

        foreach (var manga in mangas)
        {
            try
            {
                bool modified = false;

                // Sync thumbnail
                if (!string.IsNullOrEmpty(manga.LocalImageUrl))
                {
                    var thumbPath = Path.Combine(_imageStoragePath, manga.LocalImageUrl.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    if (File.Exists(thumbPath))
                    {
                        var size = new FileInfo(thumbPath).Length;
                        if (manga.ThumbnailSize != size)
                        {
                            manga.ThumbnailSize = size;
                            modified = true;
                        }
                        report.TotalThumbnailSize += size;
                    }
                }

                // Sync chapters and pages
                foreach (var chapter in manga.Chapters)
                {
                    foreach (var page in chapter.Pages)
                    {
                        if (!string.IsNullOrEmpty(page.LocalImageUrl))
                        {
                            var pagePath = Path.Combine(_imageStoragePath, page.LocalImageUrl.Replace("/", Path.DirectorySeparatorChar.ToString()));
                            if (File.Exists(pagePath))
                            {
                                var size = new FileInfo(pagePath).Length;
                                if (page.Size != size)
                                {
                                    page.Size = size;
                                    modified = true;
                                }
                                report.TotalPagesSize += size;
                            }
                        }
                    }
                }

                if (modified)
                {
                    await _mangaRepository.UpdateAsync(manga, ct);
                    report.UpdatedMangasCount++;
                }
                
                report.ProcessedMangasCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing storage for manga {MangaTitle}", manga.Title);
                report.Errors.Add($"Error syncing {manga.Title}: {ex.Message}");
            }
        }

        return report;
    }
}
