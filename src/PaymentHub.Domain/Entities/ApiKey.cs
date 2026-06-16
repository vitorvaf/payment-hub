namespace PaymentHub.Domain.Entities;

public class ApiKey
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string KeyHash { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    private ApiKey() { }

    public ApiKey(Guid id, Guid tenantId, Guid applicationId, string name, string keyHash, string keyPrefix)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (applicationId == Guid.Empty) throw new ArgumentException("ApplicationId is required.", nameof(applicationId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(keyHash)) throw new ArgumentException("KeyHash is required.", nameof(keyHash));

        Id = id;
        TenantId = tenantId;
        ApplicationId = applicationId;
        Name = name.Trim();
        KeyHash = keyHash;
        KeyPrefix = keyPrefix;
        Active = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Touch(DateTime when)
    {
        LastUsedAt = when;
    }

    public void Revoke()
    {
        Active = false;
        RevokedAt = DateTime.UtcNow;
    }
}
