using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MangaScrapper.Features.Auth;

public class Logout : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/auth/logout");
    }
    public override async Task HandleAsync(CancellationToken ct)
    {
        // Clear the existing external cookie
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        await Send.OkAsync(new { message = "Logged out successfully" }, ct);
    }
}