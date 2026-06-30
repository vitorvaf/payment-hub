namespace PaymentHub.Domain.Enums;

/// <summary>
/// Outcome of the upstream provider registration call for a webhook
/// subscription. Persisted on <c>ProviderAccount.WebhookRemoteStatus</c>.
/// </summary>
public enum ProviderWebhookRemoteStatus
{
    /// <summary>
    /// No registration attempt has been made yet (initial state).
    /// </summary>
    NotRegistered = 0,

    /// <summary>
    /// The provider confirmed the registration.
    /// </summary>
    Registered = 1,

    /// <summary>
    /// The provider rejected the registration. <c>LastError</c> on the
    /// <c>OutboxEvent</c> (or audit log) carries the category.
    /// </summary>
    RegistrationFailed = 2,

    /// <summary>
    /// Remote registration was intentionally deferred because the
    /// feature flag is off (default in production) — only the local
    /// configuration has been persisted.
    /// </summary>
    RemoteRegistrationDeferred = 3
}
