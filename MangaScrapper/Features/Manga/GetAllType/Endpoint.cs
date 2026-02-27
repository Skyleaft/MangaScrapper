using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.GetAllType;

public class Endpoint(IMangaRepository mangaRepository) : EndpointWithoutRequest<List<string>>
{
    public override void Configure()
    {
        Get("/api/manga/types");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var types = await mangaRepository.GetAllTypesAsync(ct);
        await Send.OkAsync(types, ct);
    }
}