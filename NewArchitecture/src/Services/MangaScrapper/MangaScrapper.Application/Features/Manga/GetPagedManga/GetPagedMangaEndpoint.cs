using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;

namespace MangaScrapper.Application.Features.Manga.GetPagedManga;

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
        int page = 1,
        int pageSize = 10,
        string? search = null,
        string? type = null,
        string? genre = null)
    {
        var query = new GetPagedMangaQuery(page, pageSize, search, type, genre);
        var result = await sender.Send(query, ct);

        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok(result.Value))
            : result.Error.ToHttpResult();
    }
}
