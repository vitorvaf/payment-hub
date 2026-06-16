using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Entities;

public class WebhookEvent
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public ProviderCode ProviderCode { get; private set; }
    public string? ProviderEventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string RawPayloadJson { get; private set; } = string.Empty;
    public string? Signature { get; private set; }
    public WebhookProcessingStatus ProcessingStatus { get; private set; } = WebhookProcessingStatus.Pending;
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public DateTime? NextRetryAt { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private WebhookEvent() { }

    public WebhookEvent(
        Guid id,
        ProviderCode providerCode,
        string eventType,
        string rawPayloadJson,
        string? providerEventId,
        string? signature,
        Guid? tenantId = null,
        Guid? applicationId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(rawPayloadJson))
            throw new ArgumentException("RawPayloadJson is required.", nameof(rawPayloadJson));

        Id = id;
        ProviderCode = providerCode;
        EventType = eventType.Trim();
        RawPayloadJson = rawPayloadJson;
        ProviderEventId = string.IsNullOrWhiteSpace(providerEventId) ? null : providerEventId.Trim();
        Signature = signature;
        TenantId = tenantId;
        ApplicationId = applicationId;
        ProcessingStatus = WebhookProcessingStatus.Pending;
        ReceivedAt = DateTime.UtcNow;
        UpdatedAt = ReceivedAt;
    }

    public void MarkProcessing()
    {
        ProcessingStatus = WebhookProcessingStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkProcessed()
    {
        ProcessingStatus = WebhookProcessingStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
        NextRetryAt = null;
        LastError = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error, DateTime? nextRetryAt)
    {
        ProcessingStatus = WebhookProcessingStatus.Pending;
        RetryCount += 1;
        LastError = Truncate(error, 2000);
        NextRetryAt = nextRetryAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkPermanentlyFailed(string error)
    {
        ProcessingStatus = WebhookProcessingStatus.Failed;
        RetryCount += 1;
        LastError = Truncate(error, 2000);
        NextRetryAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AssociateTenant(Guid tenantId, Guid applicationId)
    {
        TenantId = tenantId;
        ApplicationId = applicationId;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
