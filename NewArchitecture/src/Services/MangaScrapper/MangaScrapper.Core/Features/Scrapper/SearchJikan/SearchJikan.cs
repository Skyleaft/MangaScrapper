using System.Net.Http.Json;
using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Scrapper.SearchJikan;

public record SearchJikanQuery(string Title) : IQuery<List<JikanMangaSearchDto>>;

internal sealed class SearchJikanQueryHandler(IHttpClientFactory httpClientFactory)
    : IQueryHandler<SearchJikanQuery, List<JikanMangaSearchDto>>
{
    public async Task<Result<List<JikanMangaSearchDto>>> Handle(SearchJikanQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
            return new List<JikanMangaSearchDto>();

        try
        {
            var client = httpClientFactory.CreateClient();
            var response = await client.GetFromJsonAsync<JikanMangaResponse>(
                $"https://api.jikan.moe/v4/manga?q={Uri.EscapeDataString(query.Title)}&limit=10", ct);

            var items = response?.Data?.Select(d => new JikanMangaSearchDto(
                d.MalId,
                d.Title,
                d.Images?.Jpg?.ImageUrl ?? d.Images?.Webp?.ImageUrl,
                d.Score)).ToList() ?? [];

            return items;
        }
        catch (Exception ex)
        {
            return Error.Failure("Jikan.SearchFailed", ex.Message);
        }
    }
}

public sealed class SearchJikanEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("Scrapper");

        group.MapGet("/jikan/search", async (string title, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchJikanQuery(title), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SearchJikanManga");
    }
}
