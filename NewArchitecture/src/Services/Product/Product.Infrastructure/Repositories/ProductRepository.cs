using Microsoft.EntityFrameworkCore;
using NovaStack.SharedKernel.Common;
using Product.Domain.Repositories;
using Product.Domain.ValueObjects;
using Product.Infrastructure.Persistence;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace Product.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IProductRepository" />.</summary>
internal sealed class ProductRepository(ProductDbContext dbContext) : IProductRepository
{
    private readonly DbSet<DomainProduct> _products = dbContext.Products;

    public async Task<DomainProduct?> GetByIdAsync(ProductId id, CancellationToken ct = default)
    {
        return await _products.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IEnumerable<DomainProduct>> GetAllAsync(CancellationToken ct = default)
    {
        return await _products.Where(p => p.IsActive).ToListAsync(ct);
    }

    public async Task<DomainProduct?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await _products.FirstOrDefaultAsync(p => p.Name == name, ct);
    }

    public async Task<bool> ExistsAsync(ProductId id, CancellationToken ct = default)
    {
        return await _products.AnyAsync(p => p.Id == id, ct);
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        return await _products.AnyAsync(p => p.Name == name, ct);
    }

    public async Task AddAsync(DomainProduct entity, CancellationToken ct = default)
    {
        await _products.AddAsync(entity, ct);
    }

    public Task UpdateAsync(DomainProduct entity, CancellationToken ct = default)
    {
        _products.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(DomainProduct entity, CancellationToken ct = default)
    {
        _products.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<PagedList<DomainProduct>> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default)
    {
        var query = _products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                p.Name.Contains(search) ||
                p.Description.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedList<DomainProduct>(items, page, pageSize, total);
    }
}