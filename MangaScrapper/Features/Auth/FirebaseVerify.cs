using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Security;
using FirebaseAdmin.Auth;
using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Shared.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MongoDB.Driver;

namespace MangaScrapper.Features.Auth;

public class FirebaseVerify : Endpoint<FirebaseVerifyRequest, LoginResponse>
{
    private readonly MongoContext _context;

    public FirebaseVerify(MongoContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/auth/firebase");
        AllowAnonymous();
    }

    public override async Task HandleAsync(FirebaseVerifyRequest req, CancellationToken ct)
    {
        FirebaseToken decodedToken;
        try
        {
            decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(req.IdToken, cancellationToken: ct);
        }
        catch (Exception)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var uid = decodedToken.Uid;
        var email = decodedToken.Claims.ContainsKey("email") ? decodedToken.Claims["email"].ToString() : "";
        var name = decodedToken.Claims.ContainsKey("name") ? decodedToken.Claims["name"].ToString() : "";

        if (string.IsNullOrEmpty(email))
        {
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Try to find the user by FirebaseUid or Email
        var user = await _context.Users.Find(u => u.FirebaseUid == uid || u.Email == email).FirstOrDefaultAsync(ct);

        if (user == null)
        {
            // Create a new user if not found
            var username = string.IsNullOrEmpty(name) ? email.Split('@')[0] : name.Replace(" ", "").ToLower();

            // Ensure unique username
            var existingUsername = await _context.Users.Find(u => u.Username == username).FirstOrDefaultAsync(ct);
            if (existingUsername != null)
            {
                username = $"{username}_{Guid.NewGuid().ToString().Substring(0, 6)}";
            }

            user = new UserDocument
            {
                Id = Guid.CreateVersion7(),
                FirebaseUid = uid,
                Email = email,
                Username = username,
                Roles = new List<string> { "user" }
            };

            await _context.Users.InsertOneAsync(user, cancellationToken: ct);
        }
        else if (string.IsNullOrEmpty(user.FirebaseUid))
        {
            // Link existing email user with Firebase
            var update = Builders<UserDocument>.Update.Set(u => u.FirebaseUid, uid);
            await _context.Users.UpdateOneAsync(u => u.Id == user.Id, update, cancellationToken: ct);
            user.FirebaseUid = uid;
        }

        var jwtToken = JwtBearer.CreateToken(
            o =>
            {
                o.SigningKey = Config["JwtSigningKey"] ?? "a_very_secret_key_that_is_at_least_32_chars_long!!";
                o.ExpireAt = DateTime.UtcNow.AddDays(30);
                o.User.Claims.Add((ClaimTypes.NameIdentifier, user.Id.ToString()));
                o.User.Claims.Add(("Username", user.Username));
                o.User.Roles.Add(user.Roles.FirstOrDefault() ?? "user");
            });

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim("Username", user.Username),
        };
        user.Roles.ForEach(r => claims.Add(new Claim(ClaimTypes.Role, r)));
        var claimsIdentity = new ClaimsIdentity(
            claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var authProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        await Send.OkAsync(new LoginResponse
        {
            Token = jwtToken,
            UserId = user.Id,
            Expiry = DateTime.UtcNow.AddDays(30),
            Username = user.Username
        }, ct);
    }
}