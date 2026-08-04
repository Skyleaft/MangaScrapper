using System.Data;
using Dapper;
using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Results;
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.GetProductStockReport;

/// <summary>
///     Handles the <see cref="GetProductStockReportQuery" /> using Dapper to perform
///     optimized, read-only aggregation and batch queries against the database.
/// </summary>
internal sealed class GetProductStockReportQueryHandler(ISqlConnectionFactory sqlConnectionFactory)
    : IQueryHandler<GetProductStockReportQuery, ProductStockReportResponse>
{
    public async Task<Result<ProductStockReportResponse>> Handle(
        GetProductStockReportQuery query,
        CancellationToken ct)
    {
        using var connection = sqlConnectionFactory.CreateConnection();

        // Open connection if not already open (Dapper manages this, but explicitly opening is good practice)
        if (connection.State == ConnectionState.Closed) connection.Open();

        // We run a multi-query batch to pull summary stats and the low stock list in one round trip.
        // This is highly efficient and maps beautifully to Dapper.
        const string sql = @"
            -- 1. Summary Statistics
            SELECT 
                COUNT(*) AS TotalProductCount,
                COALESCE(SUM(stock_quantity), 0) AS TotalStockQuantity,
                COALESCE(SUM(price_amount * stock_quantity), 0) AS TotalInventoryValue,
                COALESCE(AVG(price_amount), 0) AS AveragePrice
            FROM products.products
            WHERE is_active = true;

            -- 2. Low Stock Products
            SELECT 
                id AS Id,
                name AS Name,
                stock_quantity AS StockQuantity,
                price_amount AS Price,
                price_currency AS Currency
            FROM products.products
            WHERE is_active = true AND stock_quantity < @Threshold
            ORDER BY stock_quantity ASC;
        ";

        var parameters = new { Threshold = query.LowStockThreshold };

        using var multi =
            await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));

        // Read first query results (Summary Stats)
        var summary = await multi.ReadFirstAsync<SummaryDto>();

        // Read second query results (Low Stock Items)
        var lowStockItems = (await multi.ReadAsync<LowStockProductResponse>()).ToList();

        var response = new ProductStockReportResponse(
            summary.TotalProductCount,
            summary.TotalStockQuantity,
            summary.TotalInventoryValue,
            summary.AveragePrice,
            lowStockItems
        );

        return response;
    }

    // Helper class to map summary metrics
    private sealed class SummaryDto
    {
        public int TotalProductCount { get; set; }
        public int TotalStockQuantity { get; set; }
        public decimal TotalInventoryValue { get; set; }
        public decimal AveragePrice { get; set; }
    }
}