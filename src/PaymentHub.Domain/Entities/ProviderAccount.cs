using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Entities;

public class ProviderAccount
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public ProviderCode ProviderCode { get; private set; }
    public ProviderEnvironment Environment { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string EncryptedCredentials { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private ProviderAccount() { }

    public ProviderAccount(
        Guid id,
        Guid tenantId,
        Guid applicationId,
        ProviderCode providerCode,
        ProviderEnvironment environment,
        string name,
        string encryptedCredentials,
        bool isDefault = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (applicationId == Guid.Empty) throw new ArgumentException("ApplicationId is required.", nameof(applicationId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(encryptedCredentials))
            throw new ArgumentException("EncryptedCredentials is required.", nameof(encryptedCredentials));

        Id = id;
        TenantId = tenantId;
        ApplicationId = applicationId;
        ProviderCode = providerCode;
        Environment = environment;
        Name = name.Trim();
        EncryptedCredentials = encryptedCredentials;
        IsDefault = isDefault;
        Active = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateCredentials(string encryptedCredentials)
    {
        EncryptedCredentials = encryptedCredentials;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsDefault()
    {
        IsDefault = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UnmarkAsDefault()
    {
        IsDefault = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Disable()
    {
        Active = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
