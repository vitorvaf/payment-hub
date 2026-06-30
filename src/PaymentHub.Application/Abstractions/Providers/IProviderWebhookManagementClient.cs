namespace PaymentHub.Application.Abstractions.Providers;

/// <summary>
/// Outcome of an attempted registration of a provider webhook at the
/// upstream. Safe enum — never includes raw HTTP body, secrets, or
/// signatures.
/// </summary>
public enum ProviderWebhookRegistrationOutcome
{
    /// <summary>
    /// The provider confirmed the webhook is registered and ready to
    /// receive events.
    /// </summary>
    Registered = 1,

    /// <summary>
    /// The provider refused the registration. Caller logs but does not
    /// surface the detail in API responses.
    /// </summary>
    RegistrationFailed = 2,
}

/// <summary>
/// Public boundary for provider webhook management (creation of the
/// webhook subscription at the upstream, e.g. <c>POST /webhooks/create</c>
/// at AbacatePay). Implementations are responsible for keeping
/// credentials, raw bodies, and signatures out of any error message or
/// log payload.
///
/// Slice 2-C ships the no-op default
/// <c>NoOpProviderWebhookManagementClient</c>. The real implementation
/// against AbacatePay will land in a follow-up slice, gated by the
/// feature flag exposed via <see cref="IProviderWebhookRegistrationFeaturePolicy"/>.
///
/// The interface lives in the Application layer so handlers can depend
/// on it without referring to Infrastructure (Clean Architecture rule).
/// Each provider can supply its own implementation in Infrastructure.
/// </summary>
public interface IProviderWebhookManagementClient
{
    /// <summary>
    /// Registers a webhook subscription at the upstream. Returns the
    /// outcome category only; never includes secret, signature or
    /// response body in the result. May throw a transient exception
    /// when network conditions allow.
    /// </summary>
    /// <param name="providerCode">
    /// Provider whose subscription is being created. Implementations
    /// for non-AbacatePay providers should reject with a typed error
    /// or surface <see cref="ProviderWebhookRegistrationOutcome.RegistrationFailed"/>.
    /// </param>
    /// <param name="protectedCredentials">
    /// AES-protected JSON blob from
    /// <c>ProviderAccount.EncryptedCredentials</c>. The implementation
    /// is the only layer that calls <c>ICredentialProtector.Unprotect</c>.
    /// </param>
    /// <param name="webhookSecret">
    /// Plaintext webhook secret to hand to the upstream so it can HMAC
    /// future events. Transient — the implementation MUST NOT persist,
    /// log, or echo it.
    /// </param>
    /// <param name="callbackUrl">
    /// Destination URL for the webhook delivery. Already SSRF-validated
    /// by the application validator.
    /// </param>
    /// <param name="events">
    /// Whitelisted event names the merchant wants to receive.
    /// </param>
    Task<ProviderWebhookRegistrationOutcome> RegisterWebhookAsync(
        Domain.Enums.ProviderCode providerCode,
        string protectedCredentials,
        string webhookSecret,
        string callbackUrl,
        IReadOnlyList<string> events,
        CancellationToken cancellationToken);
}

/// <summary>
/// Feature flag policy controlling whether the
/// <see cref="IProviderWebhookManagementClient"/> is allowed to hit the
/// upstream. Defaults to <c>false</c> for safety — only an
/// Infrastructure-level implementer can flip it on for a specific
/// provider. Implementation is registered as Singleton.
/// </summary>
public interface IProviderWebhookRegistrationFeaturePolicy
{
    /// <summary>
    /// Returns <c>true</c> only when remote registration is
    /// explicitly allowed for the requested provider. Returns
    /// <c>false</c> in any other case — feature is opt-in.
    /// </summary>
    bool IsRemoteRegistrationEnabled(Domain.Enums.ProviderCode code);
}
