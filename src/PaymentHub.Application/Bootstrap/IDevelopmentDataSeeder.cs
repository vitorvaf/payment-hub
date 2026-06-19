using PaymentHub.Application.Abstractions.Bootstrap;

namespace PaymentHub.Application.Bootstrap;

public interface IDevelopmentDataSeeder
{
    Task<DevelopmentSeedOutcome> SeedAsync(CancellationToken cancellationToken);
}
