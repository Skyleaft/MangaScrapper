using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.ProviderScrapers;

// ── Komiku ──────────────────────────────────────────────────────────────────
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

// ── Kiryuu ───────────────────────────────────────────────────────────────────
public record ScrapKiryuuMangaCommand(string MangaUrl, bool ScrapChapterPages = true, string? LinkId = null) : ICommand<ScrapperMangaDocumentResponse>;
public record GetKiryuuDetailQuery(string MangaUrl) : IQuery<ScrapperMangaDocumentResponse>;
public record SearchKiryuuQuery(ScrapperSearchRequest Request) : IQuery<List<SearchItemResponse>>;

internal sealed class ScrapKiryuuMangaCommandHandler([FromKeyedServices("kiryuu")] IProviderScrapperService scrapperService)
    : ICommandHandler<ScrapKiryuuMangaCommand, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(ScrapKiryuuMangaCommand command, CancellationToken ct)
    {
        var manga = await scrapperService.ExtractManga(command.MangaUrl, ct, command.ScrapChapterPages, command.LinkId);
        return manga is null
            ? Error.NotFound("Scrapper.NotFound", "Manga not found")
            : manga;
    }
}

internal sealed class GetKiryuuDetailQueryHandler([FromKeyedServices("kiryuu")] IProviderScrapperService scrapperService)
    : IQueryHandler<GetKiryuuDetailQuery, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(GetKiryuuDetailQuery query, CancellationToken ct)
    {
        var manga = await scrapperService.GetDetail(query.MangaUrl, ct);
        return manga;
    }
}

internal sealed class SearchKiryuuQueryHandler([FromKeyedServices("kiryuu")] IProviderScrapperService scrapperService)
    : IQueryHandler<SearchKiryuuQuery, List<SearchItemResponse>>
{
    public async Task<Result<List<SearchItemResponse>>> Handle(SearchKiryuuQuery query, CancellationToken ct)
    {
        var results = await scrapperService.SearchManga(query.Request, ct);
        return results;
    }
}

// ── Komikcast ────────────────────────────────────────────────────────────────
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

// ── MangaDex ─────────────────────────────────────────────────────────────────
public record ScrapMangaDexMangaCommand(string MangaUrl, bool ScrapChapterPages = true, string? LinkId = null) : ICommand<ScrapperMangaDocumentResponse>;
public record GetMangaDexDetailQuery(string MangaUrl) : IQuery<ScrapperMangaDocumentResponse>;
public record SearchMangaDexQuery(ScrapperSearchRequest Request) : IQuery<List<SearchItemResponse>>;

internal sealed class ScrapMangaDexMangaCommandHandler([FromKeyedServices("mangadex")] IProviderScrapperService scrapperService)
    : ICommandHandler<ScrapMangaDexMangaCommand, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(ScrapMangaDexMangaCommand command, CancellationToken ct)
    {
        var manga = await scrapperService.ExtractManga(command.MangaUrl, ct, command.ScrapChapterPages, command.LinkId);
        return manga is null
            ? Error.NotFound("Scrapper.NotFound", "Manga not found")
            : manga;
    }
}

internal sealed class GetMangaDexDetailQueryHandler([FromKeyedServices("mangadex")] IProviderScrapperService scrapperService)
    : IQueryHandler<GetMangaDexDetailQuery, ScrapperMangaDocumentResponse>
{
    public async Task<Result<ScrapperMangaDocumentResponse>> Handle(GetMangaDexDetailQuery query, CancellationToken ct)
    {
        var manga = await scrapperService.GetDetail(query.MangaUrl, ct);
        return manga;
    }
}

internal sealed class SearchMangaDexQueryHandler([FromKeyedServices("mangadex")] IProviderScrapperService scrapperService)
    : IQueryHandler<SearchMangaDexQuery, List<SearchItemResponse>>
{
    public async Task<Result<List<SearchItemResponse>>> Handle(SearchMangaDexQuery query, CancellationToken ct)
    {
        var results = await scrapperService.SearchManga(query.Request, ct);
        return results;
    }
}

// ── Endpoints Definition ──────────────────────────────────────────────────────
public sealed class ProviderScraperEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("ProviderScrapers");

        // Komiku
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

        // Kiryuu
        group.MapPost("/kiryuu", async (ProviderScrapMangaRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new ScrapKiryuuMangaCommand(req.MangaUrl, req.ScrapChapterPages, req.LinkId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/kiryuu/detail", async (string mangaUrl, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetKiryuuDetailQuery(mangaUrl), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/kiryuu/search", async ([AsParameters] ScrapperSearchRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchKiryuuQuery(req), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        // Komikcast
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

        // MangaDex
        group.MapPost("/mangadex", async (ProviderScrapMangaRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new ScrapMangaDexMangaCommand(req.MangaUrl, req.ScrapChapterPages, req.LinkId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/mangadex/detail", async (string mangaUrl, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetMangaDexDetailQuery(mangaUrl), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        group.MapGet("/mangadex/search", async ([AsParameters] ScrapperSearchRequest req, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchMangaDexQuery(req), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });
    }
}

