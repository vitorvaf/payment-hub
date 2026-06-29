using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Abstractions.Providers;

/// <summary>
/// Payload sent from <c>CreateCheckoutHandler</c> to a concrete
/// <see cref="IPaymentProviderAdapter"/>. The original positional
/// constructor is preserved for backward compatibility with
/// <c>FakePaymentProviderAdapter</c> and any existing tests; AbacatePay and
/// future providers can read the optional init-only properties populated
/// from the resolved <c>ProviderAccount</c>.
/// </summary>
public sealed record CreateCheckoutProviderRequest(
    Guid TenantId,
    Guid ApplicationId,
    Guid PaymentId,
    string ExternalReference,
    long AmountInCents,
    string Currency,
    string? CustomerEmail,
    string? CustomerName,
    string? SuccessUrl,
    string? CancelUrl,
    string? MetadataJson,
    IReadOnlyList<ProviderCheckoutItem> Items)
{
    /// <summary>
    /// Id of the resolved <c>ProviderAccount</c>. Null when the adapter does
    /// not need account-level context (e.g. Fake / Stripe / MercadoPago
    /// skeletons).
    /// </summary>
    public Guid? ProviderAccountId { get; init; }

    /// <summary>
    /// Resolved <c>ProviderEnvironment</c> as a string. Adapters that
    /// care about sandbox vs production read this; others may ignore.
    /// </summary>
    public string? ProviderEnvironment { get; init; }

    /// <summary>
    /// AES-protected credentials blob from
    /// <c>ProviderAccount.EncryptedCredentials</c>. The adapter is the
    /// only layer allowed to <c>Unprotect</c> it via
    /// <c>ICredentialProtector</c>. Adapters that do not need credentials
    /// (Fake / Stripe / MercadoPago skeletons) leave this null.
    /// </summary>
    public string? ProtectedCredentials { get; init; }
}

public sealed record ProviderCheckoutItem(
    string Id,
    string Name,
    int Quantity,
    long UnitAmount);

public sealed record CreateCheckoutProviderResult(
    bool Success,
    string? ProviderPaymentId,
    string? CheckoutUrl,
    string? ErrorMessage,
    string? RawResponseJson = null);

public sealed record ProviderWebhookRequest(
    string RawBody,
    string? Signature,
    IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>
    /// Resolved <c>ProviderAccount.Id</c>. Populated by the worker layer
    /// (e.g. <c>ProcessWebhookEventHandler</c>) when there is a definite
    /// tenant/application mapping for the inbound event. Null when the
    /// adapter does not need account-level context.
    /// </summary>
    public Guid? ProviderAccountId { get; init; }

    /// <summary>
    /// Resolved webhook secret (already in clear text — used for HMAC
    /// verification). Already protected in the database; this is the value
    /// unprotect-loaded by the handler. Transient: never persisted by
    /// the adapter or returned in any error message.
    /// </summary>
    public string? WebhookSecret { get; init; }
}

public sealed record ProviderWebhookParseResult(
    bool IsValid,
    string? ProviderEventId,
    string EventType,
    string? ProviderPaymentId,
    string? ProviderStatus,
    string? ErrorMessage,
    string? RawPayloadJson = null);