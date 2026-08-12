using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.ProviderScrapers.Komiku;

public record ScrapKomikuMangaCommand(string MangaUrl, bool ScrapChapterPages = true, string? LinkId = null) : ICommand<ScrapperMangaDocumentResponse>;
public record GetKomikuDetailQuery(string MangaUrl) : IQuery<ScrapperMangaDocumentResponse>;
public record SearchKomikuQuery(ScrapperSearchRequest Request) : IQuery<List<SearchItemResponse>>;

internal sealed class ScrapKomikuMangaCommandHandler([FromKeyedServices("komiku")] IProviderScrapperService scrapperService)
    : ICommandHandler<ScrapKomikuMangaCommand, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(ScrapKomikuMangaCommand command, CancellationToken ct)
    {
        var manga = await scrapperService.ExtractManga(command.MangaUrl, ct, command.ScrapChapterPages, command.LinkId);
        return manga is null
            ? Error.NotFound("Scrapper.NotFound", "Manga not found")
            : manga;
    }
}

internal sealed class GetKomikuDetailQueryHandler([FromKeyedServices("komiku")] IProviderScrapperService scrapperService)
    : IQueryHandler<GetKomikuDetailQuery, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(GetKomikuDetailQuery query, CancellationToken ct)
    {
        var manga = await scrapperService.GetDetail(query.MangaUrl, ct);
        return manga;
    }
}

internal sealed class SearchKomikuQueryHandler([FromKeyedServices("komiku")] IProviderScrapperService scrapperService)
    : IQueryHandler<SearchKomikuQuery, List<SearchItemResponse>>
{
    public async Task<Result<List<SearchItemResponse>>> Handle(SearchKomikuQuery query, CancellationToken ct)
    {
        var results = await scrapperService.SearchManga(query.Request, ct);
        return results;
    }
}

public sealed class KomikuEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("ProviderScrapers");

        group.MapPost("/komiku", async (ProviderScrapMangaRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new ScrapKomikuMangaCommand(req.MangaUrl, req.ScrapChapterPages, req.LinkId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/komiku/detail", async (string mangaUrl, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetKomikuDetailQuery(mangaUrl), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/komiku/search", async ([AsParameters] ScrapperSearchRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchKomikuQuery(req), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });
    }
}
