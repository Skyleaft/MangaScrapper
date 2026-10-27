using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.RateLimiting;
using MangaScrapper.Core.Repositories;
using Mapster;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.Infrastructure.Caching;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Dashboard.GetStatistics;

public record GetStatisticsQuery : IQuery<DashboardStatisticResponse>;

internal sealed class GetStatisticsQueryHandler(
    IMangaRepository mangaRepository,
    ICacheService cacheService)
    : IQueryHandler<GetStatisticsQuery, DashboardStatisticResponse>
{
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);
    private const string CacheKey = "dashboard:stats";

    public async Task<Result<DashboardStatisticResponse>> Handle(GetStatisticsQuery query, CancellationToken ct)
    {
        var response = await cacheService.GetOrSetAsync(
            CacheKey,
            async token =>
            {
                var data = await mangaRepository.GetStatisticsAsync(token);
                return data.Adapt<DashboardStatisticResponse>();
            },
            CacheExpiry,
            ct);

        return response;
    }
}

public sealed class GetStatisticsEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard");

        group.MapGet("/stats", async (ISender sender, HttpContext httpContext, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetStatisticsQuery(), ct);
            if (!res.IsSuccess)
            {
                return res.Error.ToHttpResult();
            }

            httpContext.Response.Headers.CacheControl = "private, max-age=120";
            return Results.Ok(ApiResponse.Ok(res.Value));
        })
        .WithName("GetDashboardStats")
        .WithSummary("Get System Dashboard Statistics")
        .RequireRateLimiting(RateLimitPolicies.Default)
        .Produces<ApiResponse<DashboardStatisticResponse>>();
    }
}
