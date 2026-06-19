namespace PaymentHub.Application.Abstractions.Bootstrap;

public sealed record DevelopmentSeedOutcome(
    string EnvironmentName,
    bool BootstrapEnabled,
    bool SeedRequested,
    bool SeedExecuted,
    bool TenantCreated,
    bool ApplicationCreated,
    string? Reason)
{
    public static DevelopmentSeedOutcome Skipped(string environmentName, bool bootstrapEnabled, string reason)
        => new(environmentName, bootstrapEnabled, false, false, false, false, reason);

    public static DevelopmentSeedOutcome Noop(string environmentName, bool bootstrapEnabled)
        => new(environmentName, bootstrapEnabled, true, false, false, false, "Seed requested but no items needed creation.");
}
