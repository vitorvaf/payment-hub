namespace PaymentHub.Application.Abstractions.Bootstrap;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; set; } = false;

    public bool SeedDevelopmentData { get; set; } = false;

    public bool AllowProductionBootstrap { get; set; } = false;

    public string? DevelopmentTenantSlug { get; set; } = "dev-tenant";

    public string? DevelopmentApplicationName { get; set; } = "dev-app";
}
