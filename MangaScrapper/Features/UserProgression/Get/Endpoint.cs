using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Features.UserProgression.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserProgression.Get;


public class Endpoint(IUserProgressionService service) : EndpointWithoutRequest<List<UserProgressionDocument>>
{
    public override void Configure()
    {
        Get("/api/user-progression");
    }

    public override async Task HandleAsync( CancellationToken ct)
    {
        var userId = User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ??
                     throw new Exception("User not found");
        var result = await service.GetUserProgressionsAsync(Guid.Parse(userId), ct);
        await Send.OkAsync(result, ct);
    }
}
