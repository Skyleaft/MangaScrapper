using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.GetProductById;

/// <summary>Response DTO for a single product.</summary>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string CreatedBy);

/// <summary>Query to retrieve a product by its ID.</summary>
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductResponse>;