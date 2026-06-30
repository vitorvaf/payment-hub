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

    /// <summary>
    /// Slice 2-C. Opt-in flag that allows the
    /// <c>ConfigureProviderAccountWebhookHandler</c> to call
    /// <c>POST /webhooks/create</c> at the upstream when the caller sets
    /// <c>registerRemotely=true</c>. MUST stay <c>false</c> in production —
    /// <c>appsettings.json</c> ships with this disabled and only
    /// Development overrides it (if at all).
    ///
    /// When this flag is <c>false</c> AND the caller asks for remote
    /// registration, the handler records
    /// <c>RemoteRegistrationDeferred</c> on
    /// <c>ProviderAccount.WebhookRemoteStatus</c> instead of calling
    /// the upstream.
    /// </summary>
    public bool AllowWebhookRegistration { get; init; }
}