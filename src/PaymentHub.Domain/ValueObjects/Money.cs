namespace PaymentHub.Domain.ValueObjects;

public sealed class Money : IEquatable<Money>
{
    public long Amount { get; }
    public string Currency { get; }

    private Money(long amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(long amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));
        if (currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        return new Money(amount, currency.ToUpperInvariant());
    }

    public static Money Zero(string currency) => Of(0, currency);

    public bool Equals(Money? other)
        => other is not null && Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => obj is Money m && Equals(m);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public override string ToString() => $"{Amount:0} {Currency}";
}
