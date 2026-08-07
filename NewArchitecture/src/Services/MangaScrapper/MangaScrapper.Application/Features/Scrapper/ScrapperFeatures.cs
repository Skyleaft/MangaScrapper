using System.Net.Http.Json;
using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Scrapper;

// 1. GetAllProvider
public record GetAllProviderQuery : IQuery<List<string>>;

internal sealed class GetAllProviderQueryHandler : IQueryHandler<GetAllProviderQuery, List<string>>
{
    public Task<Result<List<string>>> Handle(GetAllProviderQuery query, CancellationToken ct)
    {
        var providers = new List<string> { "Komiku", "Kiryuu", "Komikcast", "MangaDex" };
        return Task.FromResult<Result<List<string>>>(providers);
    }
}

// 2. SearchJikan
public record SearchJikanQuery(string Keyword) : IQuery<List<JikanMangaSearchDto>>;

internal sealed class SearchJikanQueryHandler(IHttpClientFactory httpClientFactory)
    : IQueryHandler<SearchJikanQuery, List<JikanMangaSearchDto>>
{
    public async Task<Result<List<JikanMangaSearchDto>>> Handle(SearchJikanQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Keyword))
            return new List<JikanMangaSearchDto>();

        try
        {
            var client = httpClientFactory.CreateClient();
            var response = await client.GetFromJsonAsync<JikanMangaResponse>(
                $"https://api.jikan.moe/v4/manga?q={Uri.EscapeDataString(query.Keyword)}&limit=10", ct);

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

// 3. ScrapChapterPages
public record ScrapChapterPagesCommand(Guid MangaId, double ChapterNumber, string Provider) : ICommand;

internal sealed class ScrapChapterPagesCommandHandler : ICommandHandler<ScrapChapterPagesCommand>
{
    public Task<Result> Handle(ScrapChapterPagesCommand command, CancellationToken ct)
    {
        // Enqueue Hangfire job in Infrastructure handler
        return Task.FromResult(Result.Success());
    }
}

// Endpoints
public sealed class ScrapperEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("Scrapper");

        group.MapGet("/providers", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetAllProviderQuery(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetAllProvider");

        group.MapGet("/jikan/search", async (string q, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchJikanQuery(q), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SearchJikan");

        group.MapPost("/scrap-chapter", async (ScrapChapterPagesCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Chapter scraping queued")) : res.Error.ToHttpResult();
        }).WithName("ScrapChapterPages");
    }
}
