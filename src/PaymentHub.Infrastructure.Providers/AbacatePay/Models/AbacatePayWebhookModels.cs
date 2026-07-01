using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Request body for <c>POST /webhooks/create</c> at the AbacatePay
/// upstream. The <c>Endpoint</c> is the URL the provider will POST
/// events to. <see cref="Secret"/> is the shared HMAC key the
/// provider will use to sign event payloads; the field is plaintext
/// on the wire (HTTPS-only) but is transient inside Payment Hub and
/// MUST NOT be logged or echoed back in responses.
/// </summary>
/// <remarks>
/// Slice 2-C.1 contract (negotiated with the audit report's "out of
/// scope" note about the official schema): the public AbacatePay
/// payload uses <c>name</c>, <c>endpoint</c>, <c>secret</c>, <c>events</c>.
/// Field names are PascalCase on this side and re-mapped to snake_case
/// via <see cref="JsonPropertyNameAttribute"/>.
/// </remarks>
public sealed class AbacatePayCreateWebhookRequest
{
    /// <summary>
    /// Friendly name for the registered webhook. We default to a
    /// deterministic identifier of the form
    /// <c>Payment Hub - {providerCode}</c> so operators can spot the
    /// Payment Hub subscriptions on the AbacatePay dashboard.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Public HTTPS endpoint that the AbacatePay upstream will POST
    /// events to. Already SSRF-validated upstream by
    /// <c>ConfigureAbacatePayWebhookRequestValidator</c>.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Plaintext shared HMAC secret the upstream will use to sign
    /// events. Persistent in <c>ProviderAccount.EncryptedCredentials</c>
    /// (encrypted at rest) but transient on this side of the call —
    /// never logged, never stored in a column on its own, never echoed
    /// in API responses.
    /// </summary>
    [JsonPropertyName("secret")]
    public string Secret { get; init; } = string.Empty;

    /// <summary>
    /// Whitelisted upstream event names this subscription wants to
    /// receive (e.g. <c>transparent.completed</c>). The application
    /// validator (<c>ConfigureAbacatePayWebhookRequestValidator</c>)
    /// enforces the four-event whitelist before this payload is
    /// materialised.
    /// </summary>
    [JsonPropertyName("events")]
    public IReadOnlyList<string> Events { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Successful <c>POST /webhooks/create</c> envelope payload from
/// AbacatePay. Carries only the upstream identifier for the new
/// subscription.
/// </summary>
public sealed class AbacatePayCreateWebhookResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

/// <summary>
/// Successful <c>GET /webhooks/list</c> envelope payload: array of
/// currently registered subscriptions. Each entry carries only the
/// upstream identifier plus the endpoint metadata required for ops
/// triage. Secrets, request bodies and signatures are never present
/// in this response shape.
/// </summary>
public sealed class AbacatePayListWebhooksResponse
{
    [JsonPropertyName("webhooks")]
    public IReadOnlyList<AbacatePayWebhookItem> Webhooks { get; init; } = Array.Empty<AbacatePayWebhookItem>();
}

/// <summary>
/// One item in the <c>webhooks</c> array returned by
/// <c>GET /webhooks/list</c>. Never carries
/// <c>apiKey</c>, <c>webhookSecret</c>, raw payloads or any other
/// sensitive material.
/// </summary>
public sealed class AbacatePayWebhookItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("events")]
    public IReadOnlyList<string> Events { get; init; } = Array.Empty<string>();
}
