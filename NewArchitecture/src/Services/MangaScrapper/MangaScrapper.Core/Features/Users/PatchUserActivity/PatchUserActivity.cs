using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Users.PatchUserActivity;

public record PatchUserActivityCommand(Guid Id) : ICommand;

public sealed class PatchUserActivityCommandHandler(IUserRepository userRepository, IClaimService claimService) : ICommandHandler<PatchUserActivityCommand>
{
    public async Task<Result> Handle(PatchUserActivityCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = claimService.GetCurrentUserId();
        if (!request.Id.ToString().Equals(currentUserId, StringComparison.OrdinalIgnoreCase))
            return Error.Forbidden();

        var user = await userRepository.GetByIdAsync(UserId.From(request.Id), cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", $"User with Id '{request.Id}' was not found.");

        user.LastActiveAt = DateTime.UtcNow;
        await userRepository.UpdateAsync(user, cancellationToken);
        return Result.Success();
    }
}

public sealed class PatchUserActivityEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");

        group.MapPatch("/{userId}", async (Guid userId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new PatchUserActivityCommand(userId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.IsSuccess)) : res.Error.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("PatchUserActivity")
        .Produces<ApiResponse<bool>>();
    }
}
