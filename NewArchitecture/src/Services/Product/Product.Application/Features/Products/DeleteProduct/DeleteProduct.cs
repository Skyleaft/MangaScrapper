using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;
using Product.Application.Common.Abstractions;
using Product.Domain.Repositories;
using Product.Domain.ValueObjects;

namespace Product.Application.Features.Products.DeleteProduct;

// ── Command ─────────────────────────────────────────────────────────────────

public sealed record DeleteProductCommand(Guid Id) : ICommand;

// ── Handler ──────────────────────────────────────────────────────────────────

internal sealed class DeleteProductCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteProductCommand>
{
    public async Task<Result> Handle(DeleteProductCommand command, CancellationToken ct)
    {
        var product = await productRepository.GetByIdAsync(ProductId.From(command.Id), ct);
        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{command.Id}' was not found.");

        product.Deactivate();
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

// ── Endpoint ─────────────────────────────────────────────────────────────────

public sealed class DeleteProductEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/products/{id:guid}", HandleAsync)
            .WithName("DeleteProduct")
            .WithSummary("Deactivate a product (soft delete)")
            .WithTags("Products")
            .RequireAuthorization() // Requires a valid JWT token from Identity Service
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new DeleteProductCommand(id), ct);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToHttpResult();
    }
}