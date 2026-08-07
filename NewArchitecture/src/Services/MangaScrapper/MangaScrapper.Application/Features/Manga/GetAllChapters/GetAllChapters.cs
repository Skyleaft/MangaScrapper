using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Manga.GetAllChapters;

public record GetAllChaptersQuery(Guid MangaId) : IQuery<List<ChapterResponse>>;

internal sealed class GetAllChaptersQueryHandler(IMangaRepository mangaRepository)
    : IQueryHandler<GetAllChaptersQuery, List<ChapterResponse>>
{
    public async Task<Result<List<ChapterResponse>>> Handle(GetAllChaptersQuery query, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(MangaId.From(query.MangaId), ct);
        if (manga is null)
            return Error.NotFound("Manga.NotFound", $"Manga with Id '{query.MangaId}' was not found.");

        var chapters = manga.Chapters.Select(c => new ChapterResponse(
            c.Id.Value,
            c.Number,
            $"Chapter {c.Number}",
            c.Link,
            c.Pages.Select(p => p.ImageUrl).ToList(),
            c.Language,
            c.ChapterProvider,
            c.ChapterProviderIcon,
            c.UploadDate,
            c.TotalView
        )).ToList();

        return chapters;
    }
}

public sealed class GetAllChaptersEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/manga/{mangaId:guid}/chapters", HandleAsync)
            .WithName("GetAllChapters")
            .WithSummary("Get all chapters for a manga")
            .WithTags("Manga")
            .Produces<ApiResponse<List<ChapterResponse>>>();
    }

    private static async Task<IResult> HandleAsync(Guid mangaId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAllChaptersQuery(mangaId), ct);
        return result.IsSuccess ? Results.Ok(ApiResponse.Ok(result.Value)) : result.Error.ToHttpResult();
    }
}
