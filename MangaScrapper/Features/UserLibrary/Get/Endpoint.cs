using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Features.UserLibrary.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserLibrary.Get;

public class Request
{
    public Guid UserId { get; set; }
}

public class Endpoint(IUserLibraryService service) : EndpointWithoutRequest<List<UserLibraryDocument>>
{
    public override void Configure()
    {
        Get("/api/user-library");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ??
                     throw new Exception("User not found");
        var result = await service.GetUserLibraryAsync(Guid.Parse(userId), ct);
        await Send.OkAsync(result, ct);
    }
}
