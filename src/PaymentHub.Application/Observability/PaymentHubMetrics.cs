using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentHub.Application.Observability;

/// <summary>
/// Single source of truth for the Payment Hub's <c>System.Diagnostics.Metrics</c>
/// instruments. Slice 9-O1 introduces observability for the minimal
/// payment / webhook / outbox flows without an external metrics backend —
/// the values surface through any <c>MeterListener</c> (OpenTelemetry, the
/// <c>dotnet-counters</c> tool, or the in-memory test collector).
///
/// <para>
/// All instruments are always registered (the slice plan's decision #3):
/// the cost is negligible and avoids conditional metrics that could mislead
/// dashboards. The class is <c>static</c> because <see cref="System.Diagnostics.Metrics.Meter"/>
/// itself is process-wide; the public surface is the call site that records
/// a measurement, not the construction of an instrument.
/// </para>
///
/// <para><b>Anti-leak tag whitelist.</b> Every tag key passed to
/// <c>Add</c>/<c>Record</c> MUST be in <see cref="AllowedTagKeys"/>.
/// High-cardinality or sensitive values (apiKey, webhookSecret, raw payload,
/// signature) are forbidden as tag values — call sites are validated by the
/// <c>scripts/agent-docs-check.sh</c> anti-leak regex and by the
/// <c>NoLeakLogTests</c> reflection suite (slice 9-O1.5).
/// </para>
/// </summary>
public static class PaymentHubMetrics
{
    /// <summary>
    /// Canonical Meter name. Other libraries (OpenTelemetry exporters, the
    /// CLI tool) filter on this value to subscribe to Payment Hub metrics.
    /// </summary>
    public const string MeterName = "PaymentHub";

    /// <summary>
    /// Process-wide Meter instance. Static so consumers never need to inject
    /// a service to record an observation; counters and histograms are safe
    /// to read from any thread.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // -----------------------------------------------------------------------
    //  Tag whitelist — every observed tag key MUST be listed here.
    //  High-cardinality, sensitive, or user-controlled strings are rejected.
    //  The list is consumed by the test collector and the docs gate.
    // -----------------------------------------------------------------------

    public static class TagKeys
    {
        public const string Provider = "provider";
        public const string Operation = "operation";
        public const string Status = "status";
        public const string ErrorCategory = "error_category";
        public const string EventType = "event_type";
        public const string Environment = "environment";
        public const string Worker = "worker";
    }

    /// <summary>
    /// Frozen whitelist used by the test collector to drop any tag key that
    /// is not listed here. Adding a new dimension requires an explicit edit
    /// to this set so the change is auditable in code review.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedTagKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        TagKeys.Provider,
        TagKeys.Operation,
        TagKeys.Status,
        TagKeys.ErrorCategory,
        TagKeys.EventType,
        TagKeys.Environment,
        TagKeys.Worker,
    };

    // -----------------------------------------------------------------------
    //  Counters — 16 instruments tracking discrete events.
    //  Convention: "_total" suffix (OpenTelemetry semantic convention).
    // -----------------------------------------------------------------------

    /// <summary>Every checkout the handler accepts (idempotent replay excluded).</summary>
    public static readonly Counter<long> CheckoutsCreatedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_checkouts_created_total",
            unit: "{checkout}",
            description: "Number of checkouts accepted by CreateCheckoutHandler.");

    /// <summary>Idempotency replays that resolved to an existing payment.</summary>
    public static readonly Counter<long> CheckoutsIdempotentReplayTotal =
        Meter.CreateCounter<long>(
            "paymenthub_checkouts_idempotent_replay_total",
            unit: "{checkout}",
            description: "Number of idempotent checkout replays that resolved to an existing payment.");

    /// <summary>Rejections emitted by the idempotency layer (hash mismatch).</summary>
    public static readonly Counter<long> CheckoutsIdempotencyConflictTotal =
        Meter.CreateCounter<long>(
            "paymenthub_checkouts_idempotency_conflict_total",
            unit: "{checkout}",
            description: "Number of checkout requests rejected because the Idempotency-Key was reused with a different payload.");

    /// <summary>Inbound provider webhooks accepted at the controller edge.</summary>
    public static readonly Counter<long> ProviderWebhooksReceivedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_provider_webhooks_received_total",
            unit: "{webhook}",
            description: "Number of inbound provider webhooks accepted by ProviderWebhooksController.");

    /// <summary>Inbound provider webhooks rejected before persistence.</summary>
    public static readonly Counter<long> ProviderWebhooksRejectedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_provider_webhooks_rejected_total",
            unit: "{webhook}",
            description: "Number of inbound provider webhooks rejected (missing signature, invalid JSON, etc.).");

    /// <summary>Webhook events transitioned to Processed by the inbox processor.</summary>
    public static readonly Counter<long> WebhookEventsProcessedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_webhook_events_processed_total",
            unit: "{event}",
            description: "Number of WebhookEvent rows the inbox processor transitioned to Processed.");

    /// <summary>Webhook events permanently failed (exhausted retries or unrecoverable).</summary>
    public static readonly Counter<long> WebhookEventsFailedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_webhook_events_failed_total",
            unit: "{event}",
            description: "Number of WebhookEvent rows the inbox processor permanently failed.");

    /// <summary>Webhook events retried (intermediate failure with backoff).</summary>
    public static readonly Counter<long> WebhookEventsRetriedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_webhook_events_retried_total",
            unit: "{event}",
            description: "Number of WebhookEvent rows the inbox processor scheduled for retry.");

    /// <summary>Outbox events successfully dispatched to the application webhook.</summary>
    public static readonly Counter<long> OutboxEventsSentTotal =
        Meter.CreateCounter<long>(
            "paymenthub_outbox_events_sent_total",
            unit: "{event}",
            description: "Number of OutboxEvent rows dispatched with success.");

    /// <summary>Outbox events that failed and were scheduled for retry.</summary>
    public static readonly Counter<long> OutboxEventsRetriedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_outbox_events_retried_total",
            unit: "{event}",
            description: "Number of OutboxEvent rows the dispatcher scheduled for retry.");

    /// <summary>Outbox events that exhausted retries and are permanently Failed.</summary>
    public static readonly Counter<long> OutboxEventsFailedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_outbox_events_failed_total",
            unit: "{event}",
            description: "Number of OutboxEvent rows the dispatcher permanently failed.");

    /// <summary>Outbox sweep recovered rows previously stuck in Processing.</summary>
    public static readonly Counter<long> OutboxOrphansRecoveredTotal =
        Meter.CreateCounter<long>(
            "paymenthub_outbox_orphans_recovered_total",
            unit: "{event}",
            description: "Number of Processing rows the orphan sweep re-enqueued to Pending.");

    /// <summary>Authorization failures (401/403) emitted by the API host.</summary>
    public static readonly Counter<long> AuthorizationDeniedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_authorization_denied_total",
            unit: "{request}",
            description: "Number of inbound requests rejected with 401/403 by ApiKeyAuthenticationMiddleware.");

    /// <summary>
    /// Slice 9-O2: checkouts that failed for any non-idempotency reason
    /// (provider error, missing default provider, etc.). Complements
    /// <see cref="CheckoutsCreatedTotal"/> + <see cref="CheckoutsIdempotencyConflictTotal"/>
    /// so dashboards can compute success_rate = created / (created + failed + conflict).
    /// </summary>
    public static readonly Counter<long> CheckoutFailedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_checkouts_failed_total",
            unit: "{checkout}",
            description: "Number of checkout requests that failed for non-idempotency reasons (provider error, validation, etc.).");

    /// <summary>
    /// Slice 9-O2: outbound provider HTTP calls (AbacatePay create/check/simulate
    /// + AbacatePay webhook management register/list). Counter increments once
    /// per attempt regardless of outcome; pair with
    /// <see cref="ProviderCallFailedTotal"/> for failure counts.
    /// </summary>
    public static readonly Counter<long> ProviderCallTotal =
        Meter.CreateCounter<long>(
            "paymenthub_provider_call_total",
            unit: "{call}",
            description: "Number of outbound provider HTTP calls attempted.");

    /// <summary>
    /// Slice 9-O2: outbound provider calls that failed with a categorized
    /// error (timeout / network / http-4xx / http-5xx / envelope-failure).
    /// Tags carry the safe <see cref="AbacatePayErrorCategory"/> name.
    /// </summary>
    public static readonly Counter<long> ProviderCallFailedTotal =
        Meter.CreateCounter<long>(
            "paymenthub_provider_call_failed_total",
            unit: "{call}",
            description: "Number of outbound provider calls that failed with a categorized error.");

    // -----------------------------------------------------------------------
    //  Histograms — 4 instruments tracking durations.
    //  Convention: "_duration_ms" suffix (milliseconds, OpenTelemetry convention).
    // -----------------------------------------------------------------------

    /// <summary>End-to-end latency of the checkout handler.</summary>
    public static readonly Histogram<double> CheckoutDurationMs =
        Meter.CreateHistogram<double>(
            "paymenthub_checkout_duration_ms",
            unit: "ms",
            description: "End-to-end duration of CreateCheckoutHandler in milliseconds.");

    /// <summary>End-to-end latency of the inbound provider webhook controller.</summary>
    public static readonly Histogram<double> ProviderWebhookDurationMs =
        Meter.CreateHistogram<double>(
            "paymenthub_provider_webhook_duration_ms",
            unit: "ms",
            description: "End-to-end duration of ProviderWebhooksController in milliseconds.");

    /// <summary>Outbound HTTP latency for the application webhook dispatcher.</summary>
    public static readonly Histogram<double> OutboxDispatchDurationMs =
        Meter.CreateHistogram<double>(
            "paymenthub_outbox_dispatch_duration_ms",
            unit: "ms",
            description: "Outbound HTTP latency of HttpApplicationWebhookDispatcher in milliseconds.");

    /// <summary>
    /// Slice 9-O2: end-to-end latency of outbound provider HTTP calls (used by
    /// <see cref="AbacatePayClient"/> and
    /// <see cref="AbacatePayWebhookManagementClient"/>). Pairs with
    /// <see cref="ProviderCallTotal"/> + <see cref="ProviderCallFailedTotal"/>
    /// for success-rate and error-budget dashboards.
    /// </summary>
    public static readonly Histogram<double> ProviderCallDurationMs =
        Meter.CreateHistogram<double>(
            "paymenthub_provider_call_duration_ms",
            unit: "ms",
            description: "End-to-end latency of outbound provider HTTP calls in milliseconds.");

    // -----------------------------------------------------------------------
    //  Convenience helpers — safe tag construction + recording.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Tags a single dimension. Returns the same <see cref="TagList"/> for
    /// fluent chaining. Throws <see cref="ArgumentException"/> when the key
    /// is not in <see cref="AllowedTagKeys"/> so misuse is caught at the
    /// call site (the test collector enforces the same rule at runtime).
    /// </summary>
    public static TagList Tag(string key, object? value)
    {
        if (!AllowedTagKeys.Contains(key))
        {
            throw new ArgumentException(
                $"Metric tag '{key}' is not in the whitelist. Add it to PaymentHubMetrics.AllowedTagKeys first.",
                nameof(key));
        }

        var tags = new TagList { { key, value } };
        return tags;
    }

    /// <summary>
    /// Tags two dimensions in one go. Both keys are validated against the
    /// whitelist; values are taken verbatim.
    /// </summary>
    public static TagList Tag(string key1, object? value1, string key2, object? value2)
    {
        if (!AllowedTagKeys.Contains(key1))
            throw new ArgumentException($"Metric tag '{key1}' is not whitelisted.", nameof(key1));
        if (!AllowedTagKeys.Contains(key2))
            throw new ArgumentException($"Metric tag '{key2}' is not whitelisted.", nameof(key2));

        return new TagList
        {
            { key1, value1 },
            { key2, value2 },
        };
    }

    /// <summary>
    /// Tags three dimensions in one go. All keys are validated against the
    /// whitelist; values are taken verbatim.
    /// </summary>
    public static TagList Tag(
        string key1, object? value1,
        string key2, object? value2,
        string key3, object? value3)
    {
        if (!AllowedTagKeys.Contains(key1))
            throw new ArgumentException($"Metric tag '{key1}' is not whitelisted.", nameof(key1));
        if (!AllowedTagKeys.Contains(key2))
            throw new ArgumentException($"Metric tag '{key2}' is not whitelisted.", nameof(key2));
        if (!AllowedTagKeys.Contains(key3))
            throw new ArgumentException($"Metric tag '{key3}' is not whitelisted.", nameof(key3));

        return new TagList
        {
            { key1, value1 },
            { key2, value2 },
            { key3, value3 },
        };
    }

    /// <summary>
    /// Records <paramref name="increment"/> on <paramref name="counter"/> with
    /// the supplied <see cref="TagList"/>. Bridges to the
    /// <c>Counter&lt;T&gt;.Add(T, params KeyValuePair&lt;string, object?&gt;[])</c>
    /// overload available in .NET 10 (the in-memory <see cref="TagList"/>
    /// is materialized into an array because there is no
    /// <c>Add(T, in TagList)</c> overload on this version of the API).
    /// </summary>
    public static void Record(this Counter<long> counter, long increment, TagList tags)
    {
        counter.Add(increment, ToPairs(tags));
    }

    /// <summary>
    /// Materialises a <see cref="TagList"/> into the array shape that
    /// <c>Counter&lt;T&gt;.Add</c> expects in .NET 10. Internal helper so
    /// call sites can keep using <see cref="Tag(string, object?)"/> and
    /// pass the result through <see cref="Record(Counter{long}, long, TagList)"/>
    /// without an explicit conversion.
    /// </summary>
    private static KeyValuePair<string, object?>[] ToPairs(TagList tags)
    {
        // TagList exposes a struct enumerator over KeyValuePair<string, object?>.
        // ToArray copies the values into a heap array (rare path, OK to allocate).
        var array = new KeyValuePair<string, object?>[tags.Count];
        var i = 0;
        foreach (var pair in tags)
        {
            array[i++] = pair;
        }
        return array;
    }
}
