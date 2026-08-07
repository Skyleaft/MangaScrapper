using MangaScrapper.Application.Common.Abstractions;
using MediatR;
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
            .WithSummary("User logout")
            .WithTags("Auth")
            .Produces<ApiResponse<object>>();
    }

    private static async Task<IResult> HandleAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new LogoutCommand(), ct);
        return result.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Logged out")) : result.Error.ToHttpResult();
    }
}
