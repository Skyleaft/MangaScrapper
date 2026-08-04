using NovaStack.SharedKernel.Common;
using NovaStack.SharedKernel.ValueObjects;

namespace Product.Domain.ValueObjects;

/// <summary>Money value object encapsulating amount and currency.</summary>
public sealed class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = Guard.NonNegative(amount, nameof(amount));
        Currency = Guard.NotNullOrWhiteSpace(currency, nameof(currency)).ToUpperInvariant();

        if (Currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Create(decimal amount, string currency)
    {
        return new Money(amount, currency);
    }

    public static Money Zero(string currency = "USD")
    {
        return new Money(0, currency);
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor)
    {
        return new Money(Amount * factor, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Cannot operate on money with different currencies: {Currency} vs {other.Currency}");
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString()
    {
        return $"{Amount:F2} {Currency}";
    }
}