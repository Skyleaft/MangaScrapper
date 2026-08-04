using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Product.Domain.ValueObjects;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace Product.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="DomainProduct" /> aggregate.</summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<DomainProduct>
{
    public void Configure(EntityTypeBuilder<DomainProduct> builder)
    {
        builder.ToTable("products");

        // Primary key — owned value object
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(
                id => id.Value,
                value => ProductId.From(value))
            .HasColumnName("id");

        // Scalar properties
        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired()
            .HasColumnName("name");

        builder.Property(p => p.Description)
            .HasMaxLength(2000)
            .IsRequired()
            .HasColumnName("description");

        builder.Property(p => p.StockQuantity)
            .IsRequired()
            .HasColumnName("stock_quantity");

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(true)
            .HasColumnName("is_active");

        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(p => p.CreatedBy)
            .HasMaxLength(500)
            .IsRequired()
            .HasColumnName("created_by");

        // Money owned entity (mapped as columns)
        builder.OwnsOne(p => p.Price, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(18, 4)
                .IsRequired();

            money.Property(m => m.Currency)
                .HasMaxLength(3)
                .HasColumnName("price_currency")
                .IsRequired();
        });

        // Indexes
        builder.HasIndex(p => p.Name).IsUnique();
        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => p.CreatedAt);

        // Ignore domain events collection — not persisted
        builder.Ignore(p => p.DomainEvents);
    }
}