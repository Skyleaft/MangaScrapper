using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Users.PatchUserHearthbeat;

[NoLogging]
public record PatchUserHearthbeatCommand : ICommand<UserHeartbeatResponse>;

public sealed class PatchUserHearthbeatCommandHandler(IUserRepository userRepository, IClaimService claimService) : ICommandHandler<PatchUserHearthbeatCommand, UserHeartbeatResponse>
{
    public async Task<Result<UserHeartbeatResponse>> Handle(PatchUserHearthbeatCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = claimService.GetCurrentUserId();

        var user = await userRepository.GetByIdAsync(UserId.From(Guid.Parse(currentUserId)), cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", $"User with Id '{currentUserId}' was not found.");

        user.LastActiveAt = DateTime.UtcNow;
        await userRepository.UpdateAsync(user, cancellationToken);
        return new UserHeartbeatResponse(user.Id.Value, user.Username, user.LastActiveAt ?? DateTime.UtcNow);
    }
}

public sealed class GetUserHearthbeatEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");

        group.MapPatch("/heartbeat", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new PatchUserHearthbeatCommand(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("PatchUserHearthbeat")
        .WithDescription("Patch user heartbeat")
        .WithTags("Users")
        .Produces<ApiResponse<UserHeartbeatResponse>>();
    }
}
