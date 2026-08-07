using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.ProviderScrapers;

// Komiku
public record ScrapKomikuMangaCommand(string Url) : ICommand;
public record SearchKomikuQuery(string Keyword) : IQuery<List<MangaSummaryResponse>>;

internal sealed class ScrapKomikuMangaCommandHandler : ICommandHandler<ScrapKomikuMangaCommand>
{
    public Task<Result> Handle(ScrapKomikuMangaCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
}

internal sealed class SearchKomikuQueryHandler : IQueryHandler<SearchKomikuQuery, List<MangaSummaryResponse>>
{
    public Task<Result<List<MangaSummaryResponse>>> Handle(SearchKomikuQuery query, CancellationToken ct) =>
        Task.FromResult<Result<List<MangaSummaryResponse>>>(new List<MangaSummaryResponse>());
}

// Kiryuu
public record ScrapKiryuuMangaCommand(string Url) : ICommand;
public record SearchKiryuuQuery(string Keyword) : IQuery<List<MangaSummaryResponse>>;

internal sealed class ScrapKiryuuMangaCommandHandler : ICommandHandler<ScrapKiryuuMangaCommand>
{
    public Task<Result> Handle(ScrapKiryuuMangaCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
}

internal sealed class SearchKiryuuQueryHandler : IQueryHandler<SearchKiryuuQuery, List<MangaSummaryResponse>>
{
    public Task<Result<List<MangaSummaryResponse>>> Handle(SearchKiryuuQuery query, CancellationToken ct) =>
        Task.FromResult<Result<List<MangaSummaryResponse>>>(new List<MangaSummaryResponse>());
}

// Komikcast
public record ScrapKomikcastMangaCommand(string Url) : ICommand;
public record SearchKomikcastQuery(string Keyword) : IQuery<List<MangaSummaryResponse>>;

internal sealed class ScrapKomikcastMangaCommandHandler : ICommandHandler<ScrapKomikcastMangaCommand>
{
    public Task<Result> Handle(ScrapKomikcastMangaCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
}

internal sealed class SearchKomikcastQueryHandler : IQueryHandler<SearchKomikcastQuery, List<MangaSummaryResponse>>
{
    public Task<Result<List<MangaSummaryResponse>>> Handle(SearchKomikcastQuery query, CancellationToken ct) =>
        Task.FromResult<Result<List<MangaSummaryResponse>>>(new List<MangaSummaryResponse>());
}

// MangaDex
public record ScrapMangaDexMangaCommand(string Url) : ICommand;
public record SearchMangaDexQuery(string Keyword) : IQuery<List<MangaSummaryResponse>>;

internal sealed class ScrapMangaDexMangaCommandHandler : ICommandHandler<ScrapMangaDexMangaCommand>
{
    public Task<Result> Handle(ScrapMangaDexMangaCommand command, CancellationToken ct) => Task.FromResult(Result.Success());
}

internal sealed class SearchMangaDexQueryHandler : IQueryHandler<SearchMangaDexQuery, List<MangaSummaryResponse>>
{
    public Task<Result<List<MangaSummaryResponse>>> Handle(SearchMangaDexQuery query, CancellationToken ct) =>
        Task.FromResult<Result<List<MangaSummaryResponse>>>(new List<MangaSummaryResponse>());
}

// Endpoints
public sealed class ProviderScraperEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrappers").WithTags("ProviderScrapers");

        // Komiku
        group.MapPost("/komiku/scrap", async (ScrapKomikuMangaCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Komiku scrap queued")) : res.Error.ToHttpResult();
        });

        group.MapGet("/komiku/search", async (string q, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchKomikuQuery(q), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        });

        // Kiryuu
        group.MapPost("/kiryuu/scrap", async (ScrapKiryuuMangaCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Kiryuu scrap queued")) : res.Error.ToHttpResult();
        });

        // Komikcast
        group.MapPost("/komikcast/scrap", async (ScrapKomikcastMangaCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Komikcast scrap queued")) : res.Error.ToHttpResult();
        });

        // MangaDex
        group.MapPost("/mangadex/scrap", async (ScrapMangaDexMangaCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "MangaDex scrap queued")) : res.Error.ToHttpResult();
        });
    }
}
