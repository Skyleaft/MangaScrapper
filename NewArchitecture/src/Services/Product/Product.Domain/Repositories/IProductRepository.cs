using NovaStack.SharedKernel.Abstractions;
using NovaStack.SharedKernel.Common;
using Product.Domain.ValueObjects;

namespace Product.Domain.Repositories;

/// <summary>Product-specific repository contract.</summary>
public interface IProductRepository : IRepository<Aggregates.Product, ProductId>
{
    Task<Aggregates.Product?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<PagedList<Aggregates.Product>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}