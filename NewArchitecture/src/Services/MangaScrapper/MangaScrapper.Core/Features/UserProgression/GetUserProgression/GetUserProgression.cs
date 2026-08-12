using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.UserProgression.GetUserProgression;

public record GetUserProgressionQuery(string UserId) : IQuery<List<UserProgressionResponse>>;

internal sealed class GetUserProgressionQueryHandler(IUserProgressionRepository progressionRepository)
    : IQueryHandler<GetUserProgressionQuery, List<UserProgressionResponse>>
{
    public async Task<Result<List<UserProgressionResponse>>> Handle(GetUserProgressionQuery query, CancellationToken ct)
    {
        var items = await progressionRepository.GetByUserIdAsync(query.UserId, ct);
        var mapped = items.Select(p => new UserProgressionResponse(p.Id, p.UserId, p.MangaId.Value, p.LastReadChapterId.Value, p.LastReadChapterNumber, p.LastReadAt)).ToList();
        return mapped;
    }
}

public sealed class GetUserProgressionEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/user-progression").WithTags("UserProgression");

        group.MapGet("/{userId}", async (string userId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetUserProgressionQuery(userId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetUserProgression")
        .Produces<ApiResponse<List<UserProgressionResponse>>>();
    }
}
