using FastEndpoints;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Manga.SyncQdrant;

public class Endpoint(QdrantService qdrantService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/manga/qdrant/sync");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await qdrantService.SyncAllAsync(ct);
        await Send.OkAsync(new { Message = "Qdrant sync completed successfully." }, cancellation: ct);
    }
}
