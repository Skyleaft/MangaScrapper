using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Shared.Models;

namespace MangaScrapper.Features.Auth;

public class UserInfo : EndpointWithoutRequest<UserInfoResponse>
{
    public override void Configure()
    {
        Get("/api/auth/me");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            await Send.OkAsync(new UserInfoResponse { IsAuthenticated = false }, ct);
            return;
        }

        await Send.OkAsync(new UserInfoResponse
        {
            IsAuthenticated = true,
            Username = User.FindFirst("Username")?.Value ?? string.Empty,
            Email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
            Roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
        }, ct);
    }
}
