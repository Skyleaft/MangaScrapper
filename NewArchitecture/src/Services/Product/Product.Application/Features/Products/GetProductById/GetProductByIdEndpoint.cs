using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.GetProductById;

/// <summary>Minimal API endpoint: GET /api/v1/products/{id}</summary>
public sealed class GetProductByIdEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/products/{id:guid}", HandleAsync)
            .WithName("GetProductById")
            .WithSummary("Get a product by ID")
            .WithTags("Products")
            .Produces<ApiResponse<ProductResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetProductByIdQuery(id), ct);

        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok(result.Value))
            : result.Error.ToHttpResult();
    }
}