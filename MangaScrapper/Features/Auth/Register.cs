using FastEndpoints;
using Isopoh.Cryptography.Argon2;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Shared.Models;
using MongoDB.Driver;

namespace MangaScrapper.Features.Auth;

public class Register : Endpoint<RegisterRequest, LoginResponse>
{
    private readonly MongoContext _context;

    public Register(MongoContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/auth/register");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
    {
        var existingUser = await _context.Users.Find(u => u.Username == req.Username).FirstOrDefaultAsync(ct);
        if (existingUser != null)
        {
            AddError("Username already exists");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var user = new UserDocument
        {
            Username = req.Username,
            PasswordHash = Argon2.Hash(req.Password),
            Email = req.Email
        };

        await _context.Users.InsertOneAsync(user, null, ct);

        await Send.OkAsync(new LoginResponse
        {
            Username = user.Username,
            Token = "Registration successful, please login", // Or generate token immediately
            Expiry = DateTime.UtcNow
        }, ct);
    }
}
