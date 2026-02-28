using System.Globalization;
using System.IO;
using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.Scrapper.FixFile;

public class Endpoint(IMangaRepository repo, IOptions<ScrapperSettings> settings) : EndpointWithoutRequest
{
    private readonly string _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath)
        ? settings.Value.ImageStoragePath
        : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);

    public override void Configure()
    {
        Get("/api/scrapper/fixfile");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var mangas = await repo.GetAllAsync(ct);
        if (mangas == null || !mangas.Any())
        {
            await Send.OkAsync("No manga found to fix.", ct);
            return;
        }

        int totalFixed = 0;
        foreach (var manga in mangas)
        {
            bool thumbnailFixed = FixThumbnailPath(manga);
            int pagesFixed = FixChapterPages(manga);

            if (thumbnailFixed || pagesFixed > 0)
            {
                totalFixed += pagesFixed;
                await repo.UpdateAsync(manga, ct);
            }
        }

        await Send.OkAsync(new { Message = "File fixing complete", TotalFixed = totalFixed }, ct);
    }

    private bool FixThumbnailPath(MangaDocument manga)
    {
        if (string.IsNullOrEmpty(manga.LocalImageUrl) || !manga.LocalImageUrl.StartsWith('/'))
            return false;

        var originalPath = manga.LocalImageUrl;
        var trimmedPath = originalPath.TrimStart('/');
        
        var oldFullPath = Path.Combine(_imageStoragePath, originalPath.Replace('/', Path.DirectorySeparatorChar));
        var newFullPath = Path.Combine(_imageStoragePath, trimmedPath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(oldFullPath) && !File.Exists(newFullPath))
        {
            File.Move(oldFullPath, newFullPath);
        }

        manga.LocalImageUrl = trimmedPath;
        return true;
    }

    private int FixChapterPages(MangaDocument manga)
    {
        int totalFixed = 0;
        foreach (var chapter in manga.Chapters)
        {
            for (int i = 0; i < chapter.Pages.Count; i++)
            {
                if (FixPage(chapter.Pages[i], i + 1))
                {
                    totalFixed++;
                }
            }
        }
        return totalFixed;
    }

    private bool FixPage(PageDocument page, int index)
    {
        if (string.IsNullOrEmpty(page.LocalImageUrl)) return false;

        bool needsUpdate = false;
        if (page.LocalImageUrl.StartsWith('/'))
        {
            page.LocalImageUrl = page.LocalImageUrl.TrimStart('/');
            needsUpdate = true;
        }

        var expectedFileName = $"{index}.webp";
        var currentFileName = Path.GetFileName(page.LocalImageUrl);
        var currentRelativeDir = Path.GetDirectoryName(page.LocalImageUrl);

        if (string.IsNullOrEmpty(currentRelativeDir)) return needsUpdate;

        if (currentFileName != expectedFileName)
        {
            var newRelativePath = Path.Combine(currentRelativeDir, expectedFileName).Replace("\\", "/");
            var oldFullPath = Path.Combine(_imageStoragePath, page.LocalImageUrl.Replace('/', Path.DirectorySeparatorChar));
            var newFullPath = Path.Combine(_imageStoragePath, newRelativePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (File.Exists(oldFullPath))
                {
                    if (!File.Exists(newFullPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
                        File.Move(oldFullPath, newFullPath);
                    }
                    page.LocalImageUrl = newRelativePath;
                    return true;
                }
                
                if (File.Exists(newFullPath))
                {
                    page.LocalImageUrl = newRelativePath;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fixing file {oldFullPath}: {ex.Message}");
            }
        }

        return needsUpdate;
    }
}
