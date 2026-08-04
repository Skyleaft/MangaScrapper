using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.CreateProduct;

/// <summary>Command to create a new product.</summary>
public sealed record CreateProductCommand(
    string Name,
    string Description,
    decimal Price,
    string Currency,
    int StockQuantity
) : ICommand<Guid>;