using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Entities;

public class OutboxEvent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public OutboxEventStatus Status { get; private set; } = OutboxEventStatus.Pending;
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? NextRetryAt { get; private set; }

    /// <summary>
    /// Slice 7-M1: timestamp the row transitioned to <see cref="OutboxEventStatus.Processing"/>.
    /// Used by the orphan sweep (<c>SweepOrphanedProcessingAsync</c>) to detect rows stuck in
    /// <c>Processing</c> after a worker crash. Cleared whenever the row leaves <c>Processing</c>
    /// (<see cref="MarkSent"/>, <see cref="MarkRetryWithCategory"/>, <see cref="MarkRetryWithStatus"/>,
    /// <see cref="MarkFailedWithCategory"/>, <see cref="MarkFailedWithStatus"/>,
    /// <see cref="RequeueOrphaned"/>).
    /// </summary>
    public DateTime? ProcessingStartedAt { get; private set; }

    /// <summary>
    /// Slice 9-O1.2: request-scoped correlation id propagated end-to-end
    /// (checkout -&gt; provider -&gt; webhook -&gt; Inbox -&gt; Outbox -&gt;
    /// application webhook). The dispatcher echoes the value on the outbound
    /// <c>X-Correlation-Id</c> header so consumers can stitch logs across the
    /// two systems. Optional: pre-9-O1.1 rows and processes that never had a
    /// middleware (background seeds) persist <c>null</c>. Column type is
    /// <c>varchar(64)</c>; values are bounded to 64 chars by
    /// <see cref="NormalizeCorrelationId"/>.
    /// </summary>
    public string? CorrelationId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private OutboxEvent() { }

    public OutboxEvent(
        Guid id,
        Guid tenantId,
        Guid applicationId,
        string eventType,
        string payloadJson,
        string? correlationId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (applicationId == Guid.Empty) throw new ArgumentException("ApplicationId is required.", nameof(applicationId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("PayloadJson is required.", nameof(payloadJson));

        Id = id;
        TenantId = tenantId;
        ApplicationId = applicationId;
        EventType = eventType.Trim();
        PayloadJson = payloadJson;
        CorrelationId = NormalizeCorrelationId(correlationId);
        Status = OutboxEventStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Sets the correlation id after construction. Used when the publisher
    /// already created the row and the accessor becomes available later (e.g.
    /// when the publisher runs outside an HTTP request but the webhook
    /// handler has already stamped the inbound <c>correlationId</c>).
    /// </summary>
    public void SetCorrelationId(string? correlationId)
    {
        CorrelationId = NormalizeCorrelationId(correlationId);
    }

    private static string? NormalizeCorrelationId(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        var trimmed = candidate.Trim();
        // Slice 9-O1.2: bounded to the column width (varchar(64)) declared
        // in the migration. Keep the cap local to the Domain layer so we
        // avoid a reverse dependency on Application.
        const int MaxLength = 64;
        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }

    /// <summary>
    /// Transitions the row to <see cref="OutboxEventStatus.Processing"/> and stamps
    /// <see cref="ProcessingStartedAt"/>. Slice 7-M1 makes the method IClock-aware so the
    /// orphan sweep can be exercised deterministically in tests; the parameterless overload
    /// is preserved for backwards compatibility with hand-rolled transitions in unit tests.
    /// </summary>
    public void MarkProcessing() => MarkProcessing(DateTime.UtcNow);

    /// <summary>
    /// Slice 7-M1: clock-injected variant of <see cref="MarkProcessing"/>. Production callers
    /// (<c>ClaimPendingForDispatchAsync</c>) pass the same <c>now</c> they use for the
    /// transactional UPDATE so the stamp matches the persisted value exactly.
    /// </summary>
    public void MarkProcessing(DateTime now)
    {
        Status = OutboxEventStatus.Processing;
        ProcessingStartedAt = now;
        UpdatedAt = now;
    }

    public void MarkSent()
    {
        Status = OutboxEventStatus.Sent;
        SentAt = DateTime.UtcNow;
        NextRetryAt = null;
        LastError = null;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Legacy: persists an arbitrary error string. Slice 7-A.7 introduces the safer
    /// <see cref="MarkRetryWithCategory(WebhookDispatcherCategory, DateTime)"/> /
    /// <see cref="MarkRetryWithStatus(WebhookDispatcherCategory, int, DateTime)"/> variants
    /// that never persist <c>ex.Message</c>. Kept for backwards compatibility with existing
    /// entity unit tests; production code in <c>OutboxDispatcherWorker</c> no longer calls it.
    /// </summary>
    public void MarkRetry(string error, DateTime nextRetryAt)
    {
        Status = OutboxEventStatus.Pending;
        RetryCount += 1;
        LastError = Truncate(error, 2000);
        NextRetryAt = nextRetryAt;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Legacy: persists an arbitrary error string. See <see cref="MarkRetry"/> for the safe
    /// replacement used by the worker after Slice 7-A.7.
    /// </summary>
    public void MarkFailed(string error)
    {
        Status = OutboxEventStatus.Failed;
        RetryCount += 1;
        LastError = Truncate(error, 2000);
        NextRetryAt = null;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Safe retry marker for failures without an HTTP status code
    /// (<see cref="WebhookDispatcherCategory.NetworkError"/>, <see cref="WebhookDispatcherCategory.Timeout"/>,
    /// <see cref="WebhookDispatcherCategory.UnprotectFailure"/>, <see cref="WebhookDispatcherCategory.MissingWebhookUrl"/>,
    /// <see cref="WebhookDispatcherCategory.UnexpectedDispatcherError"/>). Persists only the
    /// category name to <c>LastError</c>; never <c>ex.Message</c> or response bodies.
    /// </summary>
    public void MarkRetryWithCategory(WebhookDispatcherCategory category, DateTime nextRetryAt)
    {
        Status = OutboxEventStatus.Pending;
        RetryCount += 1;
        LastError = category.ToString();
        NextRetryAt = nextRetryAt;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Safe retry marker for HTTP failures. Persists
    /// <c>"{category}: status={statusCode}"</c> to <c>LastError</c>. Status code is an integer
    /// (e.g. 500, 429, 404) and is safe to persist; the response body is not.
    /// </summary>
    public void MarkRetryWithStatus(WebhookDispatcherCategory category, int statusCode, DateTime nextRetryAt)
    {
        Status = OutboxEventStatus.Pending;
        RetryCount += 1;
        LastError = FormatStatusError(category, statusCode);
        NextRetryAt = nextRetryAt;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Safe terminal failure marker for non-HTTP failures. See <see cref="MarkRetryWithCategory"/>.
    /// </summary>
    public void MarkFailedWithCategory(WebhookDispatcherCategory category)
    {
        Status = OutboxEventStatus.Failed;
        RetryCount += 1;
        LastError = category.ToString();
        NextRetryAt = null;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Safe terminal failure marker for HTTP failures. See <see cref="MarkRetryWithStatus"/>.
    /// </summary>
    public void MarkFailedWithStatus(WebhookDispatcherCategory category, int statusCode)
    {
        Status = OutboxEventStatus.Failed;
        RetryCount += 1;
        LastError = FormatStatusError(category, statusCode);
        NextRetryAt = null;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Slice 7-M1: orphan-sweep transition. Moves a stuck <c>Processing</c> row back to
    /// <c>Pending</c> so the next dispatch iteration can re-claim it. Persists the safe
    /// <see cref="WebhookDispatcherCategory.ProcessingOrphaned"/> category string to
    /// <c>LastError</c> (never the original exception, URL, body or signature). Increments
    /// <see cref="RetryCount"/> so the existing retry policy bounds total attempts.
    /// </summary>
    public void RequeueOrphaned(DateTime now, DateTime nextRetryAt)
    {
        Status = OutboxEventStatus.Pending;
        RetryCount += 1;
        LastError = WebhookDispatcherCategory.ProcessingOrphaned.ToString();
        NextRetryAt = nextRetryAt;
        ProcessingStartedAt = null;
        UpdatedAt = now;
    }

    private static string FormatStatusError(WebhookDispatcherCategory category, int statusCode)
        => $"{category}: status={statusCode}";

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}