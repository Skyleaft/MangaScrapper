using NovaStack.SharedKernel.Common;
using NovaStack.SharedKernel.Exceptions;
using Product.Domain.Events;
using Product.Domain.ValueObjects;

namespace Product.Domain.Aggregates;

/// <summary>
///     Product aggregate root. Contains all business invariants for the Product bounded context.
/// </summary>
public sealed class Product : Entity<ProductId>
{
    // ── Constructor (EF Core) ─────────────────────────────────────────────
    private Product()
    {
    }

    // ── State ──────────────────────────────────────────────────────────────
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Money Price { get; private set; } = null!;
    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;

    // ── Factory ───────────────────────────────────────────────────────────
    public static Product Create(
        ProductId id,
        string name,
        string description,
        Money price,
        int stockQuantity,
        string createdBy)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(price, nameof(price));
        Guard.NotNullOrWhiteSpace(createdBy, nameof(createdBy));

        if (stockQuantity < 0)
            throw new DomainException("Stock quantity cannot be negative.");

        var product = new Product
        {
            Id = id,
            Name = name,
            Description = description,
            Price = price,
            StockQuantity = stockQuantity,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        product.RaiseDomainEvent(new ProductCreatedDomainEvent(
            id.Value, name, price.Amount, price.Currency));

        return product;
    }

    /// <summary>
    ///     Reconstitutes a <see cref="Product" /> from persisted state without raising domain events.
    ///     Use this in repository implementations that read from a document store (e.g. MongoDB)
    ///     where EF Core's parameterless-constructor hydration path is not available.
    /// </summary>
    public static Product Reconstitute(
        ProductId id,
        string name,
        string description,
        Money price,
        int stockQuantity,
        bool isActive,
        DateTime createdAt,
        DateTime? updatedAt,
        string createdBy)
    {
        return new Product
        {
            Id = id,
            Name = name,
            Description = description,
            Price = price,
            StockQuantity = stockQuantity,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CreatedBy = createdBy
        };
    }


    // ── Behaviour ─────────────────────────────────────────────────────────
    public void Update(string name, string description, Money price)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(price, nameof(price));

        Name = name;
        Description = description;
        Price = price;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new ProductUpdatedDomainEvent(Id.Value, name, price.Amount, price.Currency));
    }

    public void AdjustStock(int delta)
    {
        var newQuantity = StockQuantity + delta;
        if (newQuantity < 0)
            throw new DomainException($"Insufficient stock. Current: {StockQuantity}, Requested delta: {delta}");

        StockQuantity = newQuantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive) throw new DomainException("Product is already inactive.");
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ProductDeletedDomainEvent(Id.Value));
    }

    public void Reactivate()
    {
        if (IsActive) throw new DomainException("Product is already active.");
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }
}