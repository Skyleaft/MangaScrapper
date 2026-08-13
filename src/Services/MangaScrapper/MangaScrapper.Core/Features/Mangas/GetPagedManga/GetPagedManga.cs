using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Mangas.GetPagedManga;

public record GetPagedMangaQuery(
    string? Search = null,
    List<string>? Genres = null,
    string? Status = null,
    string? Type = null,
    int Page = 1,
    int PageSize = 10,
    string? SortBy = "updatedAt",
    string? OrderBy = "desc") : IQuery<PagedResponse<MangaSummaryResponse>>;

public sealed class GetPagedMangaQueryHandler(
    IMangaRepository mangaRepository,
    IMangaExternalRepository mangaExternalRepository)
    : IQueryHandler<GetPagedMangaQuery, PagedResponse<MangaSummaryResponse>>
{
    public async Task<Result<PagedResponse<MangaSummaryResponse>>> Handle(
        GetPagedMangaQuery query,
        CancellationToken ct)
    {
        var data = await mangaExternalRepository.SearchAsync(
            query.Search,
            query.Genres,
            query.Status,
            query.Type,
            query.SortBy,
            query.OrderBy,
            query.Page,
            query.PageSize,
            ct);

        var ids = data.Items.Select(x => x.Id.Value).ToList();

        var mongoDocs = new Dictionary<Guid, Manga>();
        if (ids.Count > 0)
        {
            var fullDocs = await mangaRepository.GetByIdsAsync(ids, ct);
            mongoDocs = fullDocs.ToDictionary(m => m.Id.Value);
        }

        var mapped = data.Items.Select(m =>
        {
            var id = m.Id.Value;
            mongoDocs.TryGetValue(id, out var mongoDoc);

            var latest = mongoDoc?.Chapters.OrderByDescending(c => c.Number).FirstOrDefault();
            var latestSummary = latest is null
                ? new ChapterResponse(Guid.Empty, 0, "", new List<string>(), "", null, null, DateTime.MinValue, 0)
                : new ChapterResponse(latest.Id.Value, latest.Number, latest.Link, latest.Pages.Select(x => x.LocalImageUrl).ToList(), latest.Language, latest.ChapterProvider, latest.ChapterProviderIcon, latest.UploadDate, latest.TotalView);

            return new MangaSummaryResponse(
                id,
                mongoDoc?.MalId ?? 0,
                mongoDoc?.AnilistId ?? 0,
                m.Title,
                m.Author,
                m.Type,
                m.Genres,
                m.Categories,
                m.Description,
                m.ImageUrl,
                m.LocalImageUrl,
                m.ThumbnailSize,
                m.Rating,
                m.Popularity,
                mongoDoc?.Members ?? 0,
                m.ReleaseDate,
                m.Nsfw,
                m.Status,
                m.CreatedAt,
                m.UpdatedAt,
                mongoDoc?.Url ?? string.Empty,
                m.TotalView > 0 ? m.TotalView : mongoDoc?.Chapters.Sum(c => c.TotalView) ?? 0,
                latestSummary
            );
        });

        return PagedResponse<MangaSummaryResponse>.Create(
            mapped,
            data.Page,
            data.PageSize,
            data.TotalCount);
    }
}

public sealed class GetPagedMangaEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/manga", HandleAsync)
            .WithName("GetPagedManga")
            .WithSummary("Get paged list of manga")
            .WithTags("Manga")
            .Produces<ApiResponse<PagedResponse<MangaSummaryResponse>>>();
    }

    private static async Task<IResult> HandleAsync(
        ISender sender,
        CancellationToken ct,
        string? search = null,
        string[]? genres = null,
        string? status = null,
        string? type = null,
        int page = 1,
        int pageSize = 10,
        string? sortBy = "updatedAt",
        string? orderBy = "desc")
    {
        var genresList = genres?.ToList();
        var query = new GetPagedMangaQuery(search, genresList, status, type, page, pageSize, sortBy, orderBy);
        var result = await sender.Send(query, ct);

        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok(result.Value))
            : result.Error.ToHttpResult();
    }
}
