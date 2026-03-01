using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.Manga.DeleteManga;

public class Endpoint(IMangaRepository mangaRepository, IOptions<ScrapperSettings> settings) : Endpoint<Request>
{
    private readonly string _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath)
        ? settings.Value.ImageStoragePath
        : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);

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

        var cleanTitle = GetCleanTitle(manga.Title);

        foreach (var chapter in manga.Chapters)
        {
            var chapterDir = Path.Combine(_imageStoragePath, cleanTitle, chapter.Number.ToString());
            if (Directory.Exists(chapterDir))
            {
                Directory.Delete(chapterDir, true);
            }
        }

        if (!string.IsNullOrEmpty(manga.LocalImageUrl))
        {
            var thumbnailPath = Path.Combine(_imageStoragePath, manga.LocalImageUrl);
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }
        }

        await mangaRepository.DeleteAsync(r.MangaId, ct);
        await Send.OkAsync(cancellation: ct);
    }

    private static string GetCleanTitle(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Union(new[] { '?', '*', ':', '|', '<', '>', '"' })
            .ToArray();

        return string.Concat(title.Split(invalidChars));
    }
}
