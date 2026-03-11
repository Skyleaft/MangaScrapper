using FastEndpoints;
using FastEndpoints.Security;
using Isopoh.Cryptography.Argon2;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Shared.Models;
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

        if (user == null || !Argon2.Verify(req.Password, user.PasswordHash))
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

        await Send.OkAsync(new LoginResponse
        {
            Token = jwtToken,
            Expiry = DateTime.UtcNow.AddDays(1),
            Username = user.Username
        }, ct);
    }
}
