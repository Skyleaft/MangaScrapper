using System.Security.Claims;
using FluentValidation;
using Isopoh.Cryptography.Argon2;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Auth.Login;

public record LoginCommand(string Username, string Password) : ICommand<LoginResponse>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

internal sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IAuthTokenService authTokenService) : ICommandHandler<LoginCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var user = await userRepository.GetByUsernameAsync(command.Username, ct);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !Argon2.Verify(user.PasswordHash, command.Password))
        {
            return Error.Unauthorized("Auth.Failed", "Invalid credentials.");
        }

        if (!user.IsActive)
        {
            return Error.Unauthorized("Auth.Disabled", "User account is disabled.");
        }

        var (token, expiry) = authTokenService.GenerateToken(user, expiryDays: 7);
        var response = new LoginResponse(token, expiry, user.Username, user.Id.Value);

        return response;
    }
}

public sealed class LoginEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", HandleAsync)
            .WithName("Login")
            .WithSummary("User login")
            .WithTags("Auth")
            .Produces<ApiResponse<LoginResponse>>();
    }

    private static async Task<IResult> HandleAsync(
        LoginRequest request,
        ISender sender,
        IUserRepository userRepository,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var command = new LoginCommand(request.Username, request.Password);
        var result = await sender.Send(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error.ToHttpResult();
        }

        var user = await userRepository.GetByUsernameAsync(request.Username, ct);
        if (user != null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.Value.ToString()),
                new Claim(ClaimTypes.Name, user.Email),
                new Claim("Username", user.Username)
            };
            if (!string.IsNullOrEmpty(user.FirebaseUid))
            {
                claims.Add(new Claim("FirebaseUid", user.FirebaseUid));
            }
            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { AllowRefresh = true });
        }

        return Results.Ok(ApiResponse.Ok(result.Value));
    }
}
