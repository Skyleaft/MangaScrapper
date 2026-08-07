using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Auth.Logout;

public record LogoutCommand : ICommand;

internal sealed class LogoutCommandHandler : ICommandHandler<LogoutCommand>
{
    public Task<Result> Handle(LogoutCommand command, CancellationToken ct)
    {
        return Task.FromResult(Result.Success());
    }
}

public sealed class LogoutEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/logout", HandleAsync)
            .WithName("Logout")
            .WithSummary("User logout — clears cookie session")
            .WithTags("Auth")
            .Produces<ApiResponse<object>>();
    }

    private static async Task<IResult> HandleAsync(ISender sender, HttpContext httpContext, CancellationToken ct)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var result = await sender.Send(new LogoutCommand(), ct);
        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok<object?>(null, "Logged out successfully."))
            : result.Error.ToHttpResult();
    }
}
