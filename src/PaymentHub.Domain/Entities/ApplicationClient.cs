using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Entities;

public class ApplicationClient
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? WebhookUrl { get; private set; }

    /// <summary>
    /// Stores the protected webhook secret (encrypted at rest). Callers MUST pass an already-protected value.
    /// Use <see cref="HasWebhookSecret"/> for safe metadata exposure and never expose this property
    /// directly through DTOs or logs.
    /// </summary>
    public string? WebhookSecret { get; private set; }

    public ProviderCode? DefaultProvider { get; private set; }
    public ApplicationStatus Status { get; private set; } = ApplicationStatus.Active;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool HasWebhookSecret => !string.IsNullOrEmpty(WebhookSecret);

    private ApplicationClient() { }

    public ApplicationClient(
        Guid id,
        Guid tenantId,
        string name,
        string? webhookUrl = null,
        string? protectedWebhookSecret = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
        WebhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl.Trim();
        WebhookSecret = string.IsNullOrWhiteSpace(protectedWebhookSecret) ? null : protectedWebhookSecret;
        Status = ApplicationStatus.Active;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateWebhook(string? webhookUrl, string? protectedWebhookSecret)
    {
        WebhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl.Trim();
        WebhookSecret = string.IsNullOrWhiteSpace(protectedWebhookSecret) ? null : protectedWebhookSecret;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDefaultProvider(ProviderCode provider)
    {
        DefaultProvider = provider;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Suspend()
    {
        Status = ApplicationStatus.Suspended;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status = ApplicationStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }
}
