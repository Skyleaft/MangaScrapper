using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.MangaData.GetMangaById;

public record GetMangaByIdQuery(Guid Id) : IQuery<MangaSummaryResponse>;

internal sealed class GetMangaByIdQueryHandler(IMangaRepository mangaRepository)
    : IQueryHandler<GetMangaByIdQuery, MangaSummaryResponse>
{
    public async Task<Result<MangaSummaryResponse>> Handle(GetMangaByIdQuery query, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(MangaId.From(query.Id), ct);
        if (manga is null)
            return Error.NotFound("Manga.NotFound", $"Manga with Id '{query.Id}' was not found.");

        var latest = manga.Chapters.OrderByDescending(c => c.Number).FirstOrDefault();
        var latestSummary = latest is null
            ? new LatestChapterSummaryResponse(Guid.Empty, 0, 0, null, null, string.Empty, DateTime.MinValue)
            : new LatestChapterSummaryResponse(latest.Id.Value, latest.Number, latest.TotalView, latest.ChapterProvider, latest.ChapterProviderIcon, latest.Language, latest.UploadDate);

        return new MangaSummaryResponse(
            manga.Id.Value,
            manga.MalId,
            manga.AnilistId ?? 0,
            manga.Title,
            manga.Author,
            manga.Type,
            manga.Genres,
            manga.Description,
            manga.ImageUrl,
            manga.LocalImageUrl,
            manga.ThumbnailSize,
            manga.Rating,
            manga.Popularity,
            manga.Members,
            manga.ReleaseDate,
            manga.Status,
            manga.CreatedAt,
            manga.UpdatedAt,
            manga.Url,
            manga.TotalView,
            latestSummary);
    }
}

public sealed class GetMangaByIdEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/manga/{id:guid}", HandleAsync)
            .WithName("GetMangaById")
            .WithSummary("Get manga details by ID")
            .WithTags("Manga")
            .Produces<ApiResponse<MangaSummaryResponse>>();
    }

    private static async Task<IResult> HandleAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetMangaByIdQuery(id), ct);
        return result.IsSuccess ? Results.Ok(ApiResponse.Ok(result.Value)) : result.Error.ToHttpResult();
    }
}
