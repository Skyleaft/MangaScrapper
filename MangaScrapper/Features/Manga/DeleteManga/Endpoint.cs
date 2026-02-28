using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Manga.DeleteManga;

public class Endpoint(IMangaRepository mangaRepository, ScrapperService scrapperService) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/api/manga/{MangaId}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(r.MangaId, ct);
        if (manga == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Delete all chapter images from storage
        foreach (var chapter in manga.Chapters)
        {
            var chapterDir = Path.Combine(scrapperService.ImageStoragePath, manga.Title, chapter.Number.ToString());
            if (Directory.Exists(chapterDir))
            {
                Directory.Delete(chapterDir, true);
            }
        }

        // Delete thumbnail if exists
        if (!string.IsNullOrEmpty(manga.LocalImageUrl))
        {
            var thumbnailPath = Path.Combine(scrapperService.ImageStoragePath, manga.LocalImageUrl);
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }
        }

        // Delete manga from database
        await mangaRepository.DeleteAsync(r.MangaId, ct);
        
        await Send.OkAsync(cancellation:ct);
    }
}
