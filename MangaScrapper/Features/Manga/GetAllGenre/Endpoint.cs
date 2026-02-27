using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.GetAllGenre;

public class Endpoint(IMangaRepository mangaRepository) : EndpointWithoutRequest<List<string>>
{
    public override void Configure()
    {
        Get("/api/manga/genres");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var genres = await mangaRepository.GetAllGenresAsync(ct);
        await Send.OkAsync(genres, ct);
    }
}