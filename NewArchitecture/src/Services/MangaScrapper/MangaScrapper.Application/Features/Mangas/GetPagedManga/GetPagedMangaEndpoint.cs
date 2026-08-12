using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;

namespace MangaScrapper.Application.Features.Mangas.GetPagedManga;

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
        string? sortBy="updatedAt",
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
