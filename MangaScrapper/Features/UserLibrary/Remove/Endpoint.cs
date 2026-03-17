using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Features.UserLibrary.Services;

namespace MangaScrapper.Features.UserLibrary.Remove;

public class Request
{
    public Guid MangaId { get; set; }
}

public class Endpoint(IUserLibraryService service) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/api/user-library/{MangaId}");
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var userId = User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ??
                     throw new Exception("User not found");
        await service.RemoveFromLibraryAsync(Guid.Parse(userId), r.MangaId, ct);
        await Send.OkAsync(cancellation: ct);
    }
}
