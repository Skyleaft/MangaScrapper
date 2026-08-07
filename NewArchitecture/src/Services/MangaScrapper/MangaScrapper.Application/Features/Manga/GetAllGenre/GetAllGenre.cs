using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Manga.GetAllGenre;

public record GetAllGenreQuery : IQuery<List<string>>;

internal sealed class GetAllGenreQueryHandler : IQueryHandler<GetAllGenreQuery, List<string>>
{
    public Task<Result<List<string>>> Handle(GetAllGenreQuery query, CancellationToken ct)
    {
        var genres = new List<string>
        {
            "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Horror",
            "Isekai", "Mecha", "Mystery", "Romance", "Sci-Fi", "Slice of Life",
            "Sports", "Supernatural", "Thriller"
        };
        return Task.FromResult<Result<List<string>>>(genres);
    }
}

public sealed class GetAllGenreEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/manga/genres", HandleAsync)
            .WithName("GetAllGenre")
            .WithSummary("Get list of available genres")
            .WithTags("Manga")
            .Produces<ApiResponse<List<string>>>();
    }

    private static async Task<IResult> HandleAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAllGenreQuery(), ct);
        return result.IsSuccess ? Results.Ok(ApiResponse.Ok(result.Value)) : result.Error.ToHttpResult();
    }
}
