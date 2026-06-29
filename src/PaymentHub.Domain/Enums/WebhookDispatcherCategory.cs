namespace PaymentHub.Domain.Enums;

/// <summary>
/// Safe failure categories for <c>OutboxEvent.LastError</c>. Slice 7-A.7 forbids the worker
/// from persisting <c>ex.Message</c> (which can contain consumer-controlled response bodies,
/// URLs with query strings, network stack traces, or webhook secrets). Instead, the worker
/// records the category and, when applicable, the HTTP status code.
/// </summary>
public enum WebhookDispatcherCategory
{
    /// <summary>Consumer returned a non-2xx HTTP status. <c>StatusCode</c> is required.</summary>
    HttpFailure = 1,

    /// <summary>Network-level failure (DNS, connection reset, TLS handshake, etc.).</summary>
    NetworkError = 2,

    /// <summary>Dispatch exceeded the configured HTTP timeout.</summary>
    Timeout = 3,

    /// <summary><c>IWebhookSecretProtector.Unprotect</c> failed; the dispatcher refused to send an unsigned request.</summary>
    UnprotectFailure = 4,

    /// <summary>Application has no <c>WebhookUrl</c> configured.</summary>
    MissingWebhookUrl = 5,

    /// <summary>Application has no webhook secret AND the dispatcher needed one (should be unreachable in current code; reserved).</summary>
    MissingWebhookSecret = 6,

    /// <summary>Any other exception surfaced by the dispatcher. The exception itself is logged but not persisted to <c>LastError</c>.</summary>
    UnexpectedDispatcherError = 7
}