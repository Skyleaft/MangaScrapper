using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;
using Product.Application.Common.Abstractions;
using Product.Domain.Repositories;
using Product.Domain.ValueObjects;

namespace Product.Application.Features.Products.UpdateProduct;

// ── Command ─────────────────────────────────────────────────────────────────

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string Currency
) : ICommand;

// ── Validator ────────────────────────────────────────────────────────────────

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$");
    }
}

// ── Handler ──────────────────────────────────────────────────────────────────

internal sealed class UpdateProductCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateProductCommand>
{
    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken ct)
    {
        var product = await productRepository.GetByIdAsync(ProductId.From(command.Id), ct);
        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{command.Id}' was not found.");

        product.Update(command.Name, command.Description, Money.Create(command.Price, command.Currency));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

// ── Endpoint ─────────────────────────────────────────────────────────────────

public sealed class UpdateProductEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/products/{id:guid}", HandleAsync)
            .WithName("UpdateProduct")
            .WithSummary("Update an existing product")
            .WithTags("Products")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateProductRequest request,
        ISender sender,
        CancellationToken ct)
    {
        var command = new UpdateProductCommand(id, request.Name, request.Description, request.Price, request.Currency);
        var result = await sender.Send(command, ct);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToHttpResult();
    }
}

public sealed record UpdateProductRequest(
    string Name,
    string Description,
    decimal Price,
    string Currency);