using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.FixLanguage;

public class Endpoint(IMangaRepository repo) : EndpointWithoutRequest
{
    private static readonly HashSet<string> IndonesianProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Komikcast",
        "Komiku",
        "Kiryuu"
    };

    public override void Configure()
    {
        Get("/api/scrapper/fixlanguage");
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

        var totalFixed = 0;
        var mangasUpdated = 0;

        foreach (var manga in mangas)
        {
            var fixedInManga = 0;

            foreach (var chapter in manga.Chapters)
            {
                if (!string.IsNullOrEmpty(chapter.Language))
                    continue;

                if (string.IsNullOrEmpty(chapter.ChapterProvider) ||
                    !IndonesianProviders.Contains(chapter.ChapterProvider))
                    continue;

                chapter.Language = "id";
                fixedInManga++;
            }

            if (fixedInManga > 0)
            {
                totalFixed += fixedInManga;
                mangasUpdated++;
                await repo.UpdateAsync(manga, ct);
            }
        }

        await Send.OkAsync(new
        {
            Message = "Language sync complete",
            TotalChaptersFixed = totalFixed,
            MangasUpdated = mangasUpdated
        }, ct);
    }
}
