namespace PaymentHub.Application.Observability;

/// <summary>
/// Canonical log event names emitted by the Payment Hub. The names are
/// stable, lowercase + dot-separated, and consumable by log-aggregation
/// pipelines that pivot on a single field. Slice 9-O1 introduces the
/// contract so dashboards and alerts can be built against a fixed surface
/// area instead of grepping free-form <c>LogInformation</c> strings.
/// </summary>
/// <remarks>
/// <para>
/// Pair each event with the severity defined here. Do NOT log the raw value
/// of a sensitive column (apiKey, webhookSecret, raw payload, signature).
/// Use the <see cref="SafeLog"/> helpers from this namespace instead.
/// </para>
/// <para>
/// <b>Schema</b>: every <see cref="PaymentHubLogEvents"/> constant describes
/// a discrete event with a single payload schema. The <c>Event</c> shape is
/// intentionally narrow: a stable identifier + a fixed set of contextual
/// fields. Call sites must use the safe-property helpers and never inline
/// user-controlled values directly into the message template.
/// </para>
/// </remarks>
public static class PaymentHubLogEvents
{
    // -----------------------------------------------------------------------
    //  Checkout flow
    // -----------------------------------------------------------------------

    /// <summary>Checkout accepted and persisted (idempotent replay excluded).</summary>
    public const string CheckoutAccepted = "checkout.accepted";

    /// <summary>Idempotency replay resolved to an existing payment.</summary>
    public const string CheckoutIdempotentReplay = "checkout.idempotent_replay";

    /// <summary>Idempotency-Key reused with a different payload — request rejected.</summary>
    public const string CheckoutIdempotencyConflict = "checkout.idempotency_conflict";

    /// <summary>Checkout failed before persisting the Payment row.</summary>
    public const string CheckoutFailed = "checkout.failed";

    /// <summary>Provider call returned an error.</summary>
    public const string CheckoutProviderError = "checkout.provider_error";

    // -----------------------------------------------------------------------
    //  Inbound provider webhook
    // -----------------------------------------------------------------------

    /// <summary>Provider webhook received and persisted as WebhookEvent.</summary>
    public const string ProviderWebhookReceived = "provider_webhook.received";

    /// <summary>Provider webhook rejected before persistence (missing signature, etc.).</summary>
    public const string ProviderWebhookRejected = "provider_webhook.rejected";

    /// <summary>Provider webhook JSON failed to parse.</summary>
    public const string ProviderWebhookInvalidJson = "provider_webhook.invalid_json";

    /// <summary>Provider webhook HMAC signature failed verification.</summary>
    public const string ProviderWebhookSignatureInvalid = "provider_webhook.signature_invalid";

    // -----------------------------------------------------------------------
    //  Inbox (webhook processing)
    // -----------------------------------------------------------------------

    /// <summary>WebhookEvent row transitioned to Processed.</summary>
    public const string WebhookEventProcessed = "webhook_event.processed";

    /// <summary>WebhookEvent row retried (transient failure with backoff).</summary>
    public const string WebhookEventRetried = "webhook_event.retried";

    /// <summary>WebhookEvent row permanently failed.</summary>
    public const string WebhookEventFailed = "webhook_event.failed";

    /// <summary>WebhookEvent row skipped because the inbound payment was not found.</summary>
    public const string WebhookEventPaymentNotFound = "webhook_event.payment_not_found";

    /// <summary>WebhookEvent row skipped because no associated tenant/application could be resolved.</summary>
    public const string WebhookEventOrphaned = "webhook_event.orphaned";

    // -----------------------------------------------------------------------
    //  Outbox (dispatching)
    // -----------------------------------------------------------------------

    /// <summary>OutboxEvent row successfully dispatched (HTTP 2xx).</summary>
    public const string OutboxEventSent = "outbox_event.sent";

    /// <summary>OutboxEvent row retried (transient failure with backoff).</summary>
    public const string OutboxEventRetried = "outbox_event.retried";

    /// <summary>OutboxEvent row permanently failed.</summary>
    public const string OutboxEventFailed = "outbox_event.failed";

    /// <summary>OutboxEvent orphan sweep recovered rows stuck in Processing.</summary>
    public const string OutboxOrphanRecovered = "outbox.orphan_recovered";

    /// <summary>OutboxEvent dispatch skipped because ApplicationClient was not found in the tenant scope.</summary>
    public const string OutboxEventApplicationNotFound = "outbox_event.application_not_found";

    /// <summary>OutboxEvent dispatch skipped because ApplicationClient has no webhook URL configured.</summary>
    public const string OutboxEventWebhookUrlMissing = "outbox_event.webhook_url_missing";

    /// <summary>OutboxEvent dispatch aborted because the protected webhook secret could not be unprotected.</summary>
    public const string OutboxEventUnprotectFailure = "outbox_event.unprotect_failure";

    /// <summary>OutboxEvent dispatch observed a consumer-side timeout.</summary>
    public const string OutboxEventDispatchTimeout = "outbox_event.dispatch_timeout";

    /// <summary>OutboxEvent dispatch observed a transport-level network error.</summary>
    public const string OutboxEventDispatchNetworkError = "outbox_event.dispatch_network_error";

    /// <summary>OutboxEvent dispatch observed an HTTP non-2xx response.</summary>
    public const string OutboxEventDispatchHttpFailure = "outbox_event.dispatch_http_failure";

    // -----------------------------------------------------------------------
    //  Authn / Authz
    // -----------------------------------------------------------------------

    /// <summary>API key middleware accepted the request.</summary>
    public const string AuthAccepted = "auth.accepted";

    /// <summary>API key middleware rejected the request (401/403).</summary>
    public const string AuthDenied = "auth.denied";

    /// <summary>Tenant or application was not Active (403).</summary>
    public const string AuthInactive = "auth.inactive";

    // -----------------------------------------------------------------------
    //  Observability middleware
    // -----------------------------------------------------------------------

    /// <summary>CorrelationId middleware generated a fresh id (header missing or invalid).</summary>
    public const string CorrelationIdGenerated = "observability.correlation_id_generated";

    /// <summary>CorrelationId middleware preserved the inbound id (passed IsValid).</summary>
    public const string CorrelationIdAccepted = "observability.correlation_id_accepted";
}
