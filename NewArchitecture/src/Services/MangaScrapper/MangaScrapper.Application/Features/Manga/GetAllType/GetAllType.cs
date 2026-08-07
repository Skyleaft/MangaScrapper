using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Manga.GetAllType;

public record GetAllTypeQuery : IQuery<List<string>>;

internal sealed class GetAllTypeQueryHandler : IQueryHandler<GetAllTypeQuery, List<string>>
{
    public Task<Result<List<string>>> Handle(GetAllTypeQuery query, CancellationToken ct)
    {
        var types = new List<string> { "Manga", "Manhwa", "Manhua", "OEL" };
        return Task.FromResult<Result<List<string>>>(types);
    }
}

public sealed class GetAllTypeEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/manga/types", HandleAsync)
            .WithName("GetAllType")
            .WithSummary("Get list of available manga types")
            .WithTags("Manga")
            .Produces<ApiResponse<List<string>>>();
    }

    private static async Task<IResult> HandleAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAllTypeQuery(), ct);
        return result.IsSuccess ? Results.Ok(ApiResponse.Ok(result.Value)) : result.Error.ToHttpResult();
    }
}
