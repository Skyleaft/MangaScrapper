using FluentValidation;
using MangaScrapper.Application.Common.Abstractions;
using MediatR;
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

internal sealed class LoginCommandHandler : ICommandHandler<LoginCommand, LoginResponse>
{
    public Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        // Place-holder auth logic — will integrate Argon2 / Mongo User Document in Infrastructure
        if (command.Username == "admin" && command.Password == "admin")
        {
            var response = new LoginResponse(
                Token: "dummy-jwt-token",
                Expiry: DateTime.UtcNow.AddDays(3),
                Username: command.Username,
                UserId: Guid.NewGuid());

            return Task.FromResult<Result<LoginResponse>>(response);
        }

        return Task.FromResult<Result<LoginResponse>>(Error.Unauthorized("Auth.Failed", "Invalid credentials."));
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

    private static async Task<IResult> HandleAsync(LoginRequest request, ISender sender, CancellationToken ct)
    {
        var command = new LoginCommand(request.Username, request.Password);
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Results.Ok(ApiResponse.Ok(result.Value)) : result.Error.ToHttpResult();
    }
}
