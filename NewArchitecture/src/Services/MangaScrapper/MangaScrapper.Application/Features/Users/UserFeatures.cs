using FluentValidation;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using Mapster;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Users;

//GetPagedUserQuery
public record GetPagedUserQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 10, 
    string? SortBy = "createdAt",
    string? OrderBy = "desc") : IQuery<PagedResponse<UserResponse>>;

public sealed class GetPagedUserQueryHandler(IUserRepository userRepository):IQueryHandler<GetPagedUserQuery,PagedResponse<UserResponse>>
{
    public async Task<Result<PagedResponse<UserResponse>>> Handle(GetPagedUserQuery request, CancellationToken cancellationToken)
    {
        var data = await userRepository.GetPagedAsync(request.Search, request.SortBy, request.OrderBy, request.Page, request.PageSize, cancellationToken);
        var mapped = data.Items.Select(usr =>
        {
            return new UserResponse(usr.Id.Value,usr.Username,usr.Email,usr.Roles,usr.IsActive,usr.FirebaseUid,usr.CreatedAt);
        });
        
        return PagedResponse<UserResponse>.Create(
            mapped,
            data.Page,
            data.PageSize,
            data.TotalCount);
    }
}

//GetUser By ID
public record GetUserByIdQuery(Guid Id) : IQuery<UserResponse>;

public sealed class GetUserByIdQueryHandler(IUserRepository userRepository):IQueryHandler<GetUserByIdQuery,UserResponse>
{
    public async Task<Result<UserResponse>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(UserId.From(request.Id), cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", $"User with Id '{request.Id}' was not found.");
        return new UserResponse(user.Id.Value, user.Username, user.Email,user.Roles,user.IsActive,user.FirebaseUid,user.CreatedAt);
    }
}

//Patch User Activity

public record PatchUserActivityCommand(Guid Id) : ICommand;

public sealed class PatchUserActivityCommandHandler(IUserRepository userRepository, IClaimService claimService):ICommandHandler<PatchUserActivityCommand>
{
    public  async Task<Result> Handle(PatchUserActivityCommand request, CancellationToken cancellationToken)
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

// Endpoints
public sealed class UserProgressionEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");
        
        group.MapGet("/", async (ISender sender, CancellationToken ct,
                string? search = null,
                int page = 1,
                int pageSize = 10, 
                string? sortBy="updatedAt",
                string? orderBy = "desc" ) =>
        {
            var res = await sender.Send(new GetPagedUserQuery(search, page, pageSize, sortBy, orderBy), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetPagedUser")
        .Produces<ApiResponse<PagedResponse<UserResponse>>>();

        group.MapGet("/{userId}", async (Guid userId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetUserByIdQuery(userId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetUserById")
        .Produces<ApiResponse<UserResponse>>();
        
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
