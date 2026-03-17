using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Features.UserProgression.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserProgression.GetManga;

public class Request
{
    public Guid MangaId { get; set; }
}

public class Endpoint(IUserProgressionService service) : Endpoint<Request, UserProgressionDocument>
{
    public override void Configure()
    {
        Get("/api/user-progression/{MangaId}");
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var userId = User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ??
                     throw new Exception("User not found");
        
        var result = await service.GetMangaProgressionAsync(Guid.Parse(userId), r.MangaId, ct);
        if (result == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        await Send.OkAsync(result, ct);
    }
}
