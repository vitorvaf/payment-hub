using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PaymentHub.Domain.ValueObjects;

namespace PaymentHub.Infrastructure.Postgres.Configurations;

public class MoneyToLongConverter : ValueConverter<Money, long>
{
    public MoneyToLongConverter() : base(
        v => v.Amount,
        v => Money.Of(v, "BRL"))
    { }
}
