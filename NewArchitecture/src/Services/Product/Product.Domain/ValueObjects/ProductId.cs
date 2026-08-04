using NovaStack.SharedKernel.ValueObjects;

namespace Product.Domain.ValueObjects;

/// <summary>Strongly-typed Product identifier.</summary>
public sealed class ProductId : ValueObject
{
    private ProductId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static ProductId New()
    {
        return new ProductId(Guid.CreateVersion7());
    }

    public static ProductId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ProductId cannot be an empty GUID.", nameof(value));
        return new ProductId(value);
    }

    public static ProductId From(string value)
    {
        return Guid.TryParse(value, out var id)
            ? From(id)
            : throw new ArgumentException($"'{value}' is not a valid ProductId.", nameof(value));
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator Guid(ProductId id)
    {
        return id.Value;
    }

    public static implicit operator ProductId(Guid id)
    {
        return From(id);
    }
}