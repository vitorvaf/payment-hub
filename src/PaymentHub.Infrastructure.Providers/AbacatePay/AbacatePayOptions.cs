namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Strongly-typed configuration for the AbacatePay provider. Bound from the
/// <c>Providers:AbacatePay</c> configuration section. No real API key lives here —
/// credentials are stored on <c>ProviderAccount.EncryptedCredentials</c> and
/// unprotected per-request via <c>ICredentialProtector</c>.
/// </summary>
public sealed class AbacatePayOptions
{
    /// <summary>
    /// Configuration section name consumed by <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// via <c>services.Configure&lt;AbacatePayOptions&gt;(configuration.GetSection(...))</c>.
    /// </summary>
    public const string SectionName = "Providers:AbacatePay";

    /// <summary>
    /// Base URL of the AbacatePay REST API. Defaults to the public sandbox/devMode URL.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.abacatepay.com/v2";

    /// <summary>
    /// HTTP timeout applied to <see cref="System.Net.Http.HttpClient"/> requests. Default 30s.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Opt-in flag that allows the adapter to call <c>/transparents/simulate-payment</c>
    /// (used only in AbacatePay sandbox devMode). MUST stay <c>false</c> in production —
    /// <c>appsettings.json</c> ships with this disabled and only Development overrides it.
    /// </summary>
    public bool AllowDevModeSimulation { get; init; }
}