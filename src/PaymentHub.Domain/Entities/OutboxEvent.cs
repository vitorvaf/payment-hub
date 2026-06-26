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
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private OutboxEvent() { }

    public OutboxEvent(
        Guid id,
        Guid tenantId,
        Guid applicationId,
        string eventType,
        string payloadJson)
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
        Status = OutboxEventStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void MarkProcessing()
    {
        Status = OutboxEventStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkSent()
    {
        Status = OutboxEventStatus.Sent;
        SentAt = DateTime.UtcNow;
        NextRetryAt = null;
        LastError = null;
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
        UpdatedAt = DateTime.UtcNow;
    }

    private static string FormatStatusError(WebhookDispatcherCategory category, int statusCode)
        => $"{category}: status={statusCode}";

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}