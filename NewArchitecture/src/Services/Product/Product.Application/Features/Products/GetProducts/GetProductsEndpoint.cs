using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using Product.Application.Common.Abstractions;
using Product.Application.Features.Products.GetProductById;

namespace Product.Application.Features.Products.GetProducts;

/// <summary>Minimal API endpoint: GET /api/v1/products</summary>
public sealed class GetProductsEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/products", HandleAsync)
            .WithName("GetProducts")
            .WithSummary("Get paginated list of products")
            .WithTags("Products")
            .Produces<ApiResponse<PagedResponse<ProductResponse>>>();
    }

    private static async Task<IResult> HandleAsync(
        ISender sender,
        CancellationToken ct,
        int page = 1,
        int pageSize = 10,
        string? search = null,
        string? sortBy = null,
        bool descending = false)
    {
        var query = new GetProductsQuery(page, pageSize, search, sortBy, descending);
        var result = await sender.Send(query, ct);

        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok(result.Value))
            : result.Error.ToHttpResult();
    }
}