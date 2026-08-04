using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.CreateProduct;

/// <summary>Minimal API endpoint for creating a product (POST /api/v1/products).</summary>
public sealed class CreateProductEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/products", HandleAsync)
            .WithName("CreateProduct")
            .WithSummary("Create a new product")
            .WithDescription("Creates a new product in the catalog.")
            .WithTags("Products")
            .Produces<ApiResponse<Guid>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(); // Uncomment to enable auth
    }

    private static async Task<IResult> HandleAsync(
        CreateProductCommand command,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);

        return result.IsSuccess
            ? Results.Created(
                $"/api/v1/products/{result.Value}",
                ApiResponse.Ok(result.Value, "Product created successfully."))
            : result.Error.ToHttpResult();
    }
}