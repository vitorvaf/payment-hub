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

    public void MarkRetry(string error, DateTime nextRetryAt)
    {
        Status = OutboxEventStatus.Pending;
        RetryCount += 1;
        LastError = Truncate(error, 2000);
        NextRetryAt = nextRetryAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = OutboxEventStatus.Failed;
        RetryCount += 1;
        LastError = Truncate(error, 2000);
        NextRetryAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
