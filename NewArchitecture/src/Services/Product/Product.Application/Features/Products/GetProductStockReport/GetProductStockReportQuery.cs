using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.GetProductStockReport;

/// <summary>
///     DTO representing a low-stock product in the report.
/// </summary>
public sealed record LowStockProductResponse(
    Guid Id,
    string Name,
    int StockQuantity,
    decimal Price,
    string Currency);

/// <summary>
///     Response DTO containing stock statistics and low stock items.
/// </summary>
public sealed record ProductStockReportResponse(
    int TotalProductCount,
    int TotalStockQuantity,
    decimal TotalInventoryValue,
    decimal AveragePrice,
    IReadOnlyList<LowStockProductResponse> LowStockProducts);

/// <summary>
///     Query to retrieve a comprehensive stock/inventory statistics report using Dapper.
/// </summary>
/// <param name="LowStockThreshold">Products with stock below this number are flagged.</param>
public sealed record GetProductStockReportQuery(int LowStockThreshold = 10)
    : IQuery<ProductStockReportResponse>;