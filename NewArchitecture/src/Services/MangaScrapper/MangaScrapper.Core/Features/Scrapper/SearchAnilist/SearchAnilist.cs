using MangaScrapper.Core.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Scrapper.SearchAnilist;

public record SearchAnilistQuery(string Title) : IQuery<List<AnilistMedia>>;

internal sealed class SearchAnilistQueryHandler(
    [FromKeyedServices("komiku")] IProviderScrapperService scrapperService)
    : IQueryHandler<SearchAnilistQuery, List<AnilistMedia>>
{
    public async Task<Result<List<AnilistMedia>>> Handle(SearchAnilistQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
            return new List<AnilistMedia>();

        try
        {
            var items = await scrapperService.SearchAnilist(query.Title, ct);
            return items;
        }
        catch (Exception ex)
        {
            return Error.Failure("Anilist.SearchFailed", ex.Message);
        }
    }
}

public sealed class SearchAnilistEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("Scrapper");

        group.MapGet("/anilist/search", async (string title, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchAnilistQuery(title), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SearchAnilistManga");
    }
}
