using NovaStack.SharedKernel.Abstractions;

namespace Product.Domain.Events;

/// <summary>Raised when a product is successfully created.</summary>
public sealed record ProductCreatedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid ProductId,
    string Name,
    decimal Price,
    string Currency
) : IDomainEvent
{
    public ProductCreatedDomainEvent(Guid productId, string name, decimal price, string currency)
        : this(Guid.CreateVersion7(), DateTime.UtcNow, productId, name, price, currency)
    {
    }
}

/// <summary>Raised when a product is updated.</summary>
public sealed record ProductUpdatedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid ProductId,
    string Name,
    decimal Price,
    string Currency
) : IDomainEvent
{
    public ProductUpdatedDomainEvent(Guid productId, string name, decimal price, string currency)
        : this(Guid.CreateVersion7(), DateTime.UtcNow, productId, name, price, currency)
    {
    }
}

/// <summary>Raised when a product is deleted.</summary>
public sealed record ProductDeletedDomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid ProductId
) : IDomainEvent
{
    public ProductDeletedDomainEvent(Guid productId)
        : this(Guid.CreateVersion7(), DateTime.UtcNow, productId)
    {
    }
}