using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Security;
using Isopoh.Cryptography.Argon2;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Shared.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MongoDB.Driver;

namespace MangaScrapper.Features.Auth;

public class Login : Endpoint<LoginRequest, LoginResponse>
{
    private readonly MongoContext _context;

    public Login(MongoContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var user = await _context.Users.Find(u => u.Username == req.Username).FirstOrDefaultAsync(ct);

        if (user == null || !Argon2.Verify(user.PasswordHash, req.Password))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var jwtToken = JwtBearer.CreateToken(
            o =>
            {
                o.SigningKey = Config["JwtSigningKey"] ?? "a_very_secret_key_that_is_at_least_32_chars_long!!";
                o.ExpireAt = DateTime.UtcNow.AddDays(1);
                o.User.Claims.Add(("Username", user.Username));
                o.User.Roles.Add("Admin");
            });
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Email),
            new Claim("Username", user.Username),
            new Claim(ClaimTypes.Role, "Administrator"),
        };
        var claimsIdentity = new ClaimsIdentity(
            claims, CookieAuthenticationDefaults.AuthenticationScheme);
        
        var authProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
            // Refreshing the authentication session should be allowed.

            //ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            // The time at which the authentication ticket expires. A 
            // value set here overrides the ExpireTimeSpan option of 
            // CookieAuthenticationOptions set with AddCookie.

            //IsPersistent = true,
            // Whether the authentication session is persisted across 
            // multiple requests. When used with cookies, controls
            // whether the cookie's lifetime is absolute (matching the
            // lifetime of the authentication ticket) or session-based.

            //IssuedUtc = <DateTimeOffset>,
            // The time at which the authentication ticket was issued.

            //RedirectUri = <string>
            // The full path or absolute URI to be used as an http 
            // redirect response value.
        };
        
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, 
            new ClaimsPrincipal(claimsIdentity), 
            authProperties);
        
        await Send.OkAsync(new LoginResponse
        {
            Token = jwtToken,
            Expiry = DateTime.UtcNow.AddDays(1),
            Username = user.Username
        }, ct);
    }
}
