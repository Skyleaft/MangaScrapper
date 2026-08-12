using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.ProviderScrapers.Komikcast;

public record ScrapKomikcastMangaCommand(string MangaUrl, bool ScrapChapterPages = true, string? LinkId = null) : ICommand<ScrapperMangaDocumentResponse>;
public record GetKomikcastDetailQuery(string MangaUrl) : IQuery<ScrapperMangaDocumentResponse>;
public record SearchKomikcastQuery(ScrapperSearchRequest Request) : IQuery<List<SearchItemResponse>>;

internal sealed class ScrapKomikcastMangaCommandHandler([FromKeyedServices("komikcast")] IProviderScrapperService scrapperService)
    : ICommandHandler<ScrapKomikcastMangaCommand, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(ScrapKomikcastMangaCommand command, CancellationToken ct)
    {
        var manga = await scrapperService.ExtractManga(command.MangaUrl, ct, command.ScrapChapterPages, command.LinkId);
        return manga is null
            ? Error.NotFound("Scrapper.NotFound", "Manga not found")
            : manga;
    }
}

internal sealed class GetKomikcastDetailQueryHandler([FromKeyedServices("komikcast")] IProviderScrapperService scrapperService)
    : IQueryHandler<GetKomikcastDetailQuery, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(GetKomikcastDetailQuery query, CancellationToken ct)
    {
        var manga = await scrapperService.GetDetail(query.MangaUrl, ct);
        return manga;
    }
}

internal sealed class SearchKomikcastQueryHandler([FromKeyedServices("komikcast")] IProviderScrapperService scrapperService)
    : IQueryHandler<SearchKomikcastQuery, List<SearchItemResponse>>
{
    public async Task<Result<List<SearchItemResponse>>> Handle(SearchKomikcastQuery query, CancellationToken ct)
    {
        var results = await scrapperService.SearchManga(query.Request, ct);
        return results;
    }
}

public sealed class KomikcastEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("ProviderScrapers");

        group.MapPost("/komikcast", async (ProviderScrapMangaRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new ScrapKomikcastMangaCommand(req.MangaUrl, req.ScrapChapterPages, req.LinkId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/komikcast/detail", async (string mangaUrl, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetKomikcastDetailQuery(mangaUrl), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/komikcast/search", async ([AsParameters] ScrapperSearchRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchKomikcastQuery(req), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });
    }
}
