namespace PaymentHub.Domain.Entities;

public class IdempotencyKey
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public Guid PaymentId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private IdempotencyKey() { }

    public IdempotencyKey(Guid id, Guid tenantId, Guid applicationId, string key, string requestHash, Guid paymentId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (applicationId == Guid.Empty) throw new ArgumentException("ApplicationId is required.", nameof(applicationId));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));
        if (paymentId == Guid.Empty) throw new ArgumentException("PaymentId is required.", nameof(paymentId));

        Id = id;
        TenantId = tenantId;
        ApplicationId = applicationId;
        Key = key.Trim();
        RequestHash = requestHash;
        PaymentId = paymentId;
        CreatedAt = DateTime.UtcNow;
    }
}
