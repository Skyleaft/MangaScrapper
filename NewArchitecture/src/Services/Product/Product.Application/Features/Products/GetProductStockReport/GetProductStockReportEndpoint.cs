using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using Product.Application.Common.Abstractions;

namespace Product.Application.Features.Products.GetProductStockReport;

/// <summary>
///     Minimal API endpoint: GET /api/v1/products/stock-report
/// </summary>
public sealed class GetProductStockReportEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/products/stock-report", HandleAsync)
            .WithName("GetProductStockReport")
            .WithSummary("Get a stock statistics and inventory report")
            .WithTags("Products")
            .Produces<ApiResponse<ProductStockReportResponse>>();
    }

    private static async Task<IResult> HandleAsync(
        ISender sender,
        CancellationToken ct,
        int lowStockThreshold = 10)
    {
        var query = new GetProductStockReportQuery(lowStockThreshold);
        var result = await sender.Send(query, ct);

        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok(result.Value))
            : result.Error.ToHttpResult();
    }
}