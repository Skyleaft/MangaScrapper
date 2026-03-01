using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.Manga.Delete_Chapter;

public class Endpoint(IMangaRepository mangaRepository, IOptions<ScrapperSettings> settings) : Endpoint<Request>
{
    private readonly string _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath)
        ? settings.Value.ImageStoragePath
        : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);

    public override void Configure()
    {
        Delete("/api/manga/{MangaId}/chapter/{ChapterId}");
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

        var chapter = manga.Chapters.FirstOrDefault(c => c.Id == r.ChapterId);
        if (chapter == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var cleanTitle = GetCleanTitle(manga.Title);
        var chapterDir = Path.Combine(_imageStoragePath, cleanTitle, chapter.Number.ToString());
        if (Directory.Exists(chapterDir))
        {
            Directory.Delete(chapterDir, true);
        }

        var deleted = await mangaRepository.DeleteChapterAsync(r.MangaId, r.ChapterId, ct);
        if (!deleted)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

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
