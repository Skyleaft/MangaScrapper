using Microsoft.EntityFrameworkCore;
using NovaStack.Infrastructure.Persistence;
using NovaStack.SharedKernel.Abstractions;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace Product.Infrastructure.Persistence;

/// <summary>EF Core DbContext for the Product service. Also acts as the Unit of Work.</summary>
public sealed class ProductDbContext : DbContextBase, IUnitOfWork
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options)
    {
    }

    public DbSet<DomainProduct> Products => Set<DomainProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductDbContext).Assembly);
        modelBuilder.HasDefaultSchema("products");
    }
}