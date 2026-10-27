using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.RateLimiting;
using MangaScrapper.Core.Repositories;
using Mapster;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.Infrastructure.Caching;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Mangas.GetTrending;

public record GetTrendingQuery(string? Search, List<string>? Genres, string? Status, string? Type, int Page = 1, int PageSize = 10) : IQuery<PagedResponse<MangaSummaryResponse>>;

internal sealed class GetTrendingQueryHandler(IMangaRepository repository, ICacheService cacheService)
    : IQueryHandler<GetTrendingQuery, PagedResponse<MangaSummaryResponse>>
{
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(15);

    public async Task<Result<PagedResponse<MangaSummaryResponse>>> Handle(GetTrendingQuery request, CancellationToken cancellationToken)
    {
        var genresKey = request.Genres is { Count: > 0 }
            ? string.Join(",", request.Genres.OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
            : "";
        var cacheKey = $"manga:trending:{request.Search?.Trim().ToLowerInvariant()}:{genresKey}:{request.Status?.ToLowerInvariant()}:{request.Type?.ToLowerInvariant()}:{request.Page}:{request.PageSize}";

        var response = await cacheService.GetOrSetAsync(
            cacheKey,
            async ct =>
            {
                var data = await repository.GetTrendingAsync(
                    request.Search, request.Genres, request.Status, request.Type,
                    request.Page, request.PageSize, ct);

                return PagedResponse<MangaSummaryResponse>.Create(
                    data.Items.Select(x => x.Adapt<MangaSummaryResponse>()),
                    request.Page,
                    request.PageSize,
                    data.TotalCount);
            },
            CacheExpiry,
            cancellationToken);

        return response;
    }
}

//endpoint
public sealed class GetTrendingQueryEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/manga/trending", HandleAsync)
            .WithName("GetTrending")
            .WithSummary("Get Trending Manga")
            .WithTags("Manga")
            .RequireRateLimiting(RateLimitPolicies.Default)
            .Produces<ApiResponse<PagedResponse<MangaSummaryResponse>>>();
    }

    private static async Task<IResult> HandleAsync(
        ISender sender,
        HttpContext httpContext,
        CancellationToken ct,
        string? search,
        string[]? genres,
        string? status,
        string? type,
        int page = 1,
        int pageSize = 10)
    {
        var sanitizedPage = Math.Max(1, page);
        var sanitizedPageSize = Math.Clamp(pageSize, 1, 50);

        var result = await sender.Send(new GetTrendingQuery(search, genres?.ToList(), status, type, sanitizedPage, sanitizedPageSize), ct);
        if (!result.IsSuccess)
        {
            return result.Error.ToHttpResult();
        }

        httpContext.Response.Headers.CacheControl = "public, max-age=600";
        return Results.Ok(ApiResponse.Ok(result.Value));
    }
}
