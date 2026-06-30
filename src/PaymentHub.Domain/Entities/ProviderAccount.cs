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

    // Non-sensitive webhook configuration (Slice 2-C).
    // `webhookSecret` itself is NEVER stored here — it travels only
    // inside `EncryptedCredentials` as JSON `{ apiKey, webhookSecret, ... }`
    // (or legacy `{ apiKey, secret }`). These fields describe the
    // merchant-facing target and the registration status; they are
    // safe to expose via API responses.
    public string? WebhookCallbackUrl { get; private set; }
    /// <summary>
    /// JSON array of event names subscribed at the provider.
    /// Stored as opaque text (NOT a secret — at most a configurational
    /// "what events do we want"). Persisted as `jsonb` so SQL can list
    /// by event later if needed.
    /// </summary>
    public string? WebhookEvents { get; private set; }
    public DateTime? WebhookConfiguredAt { get; private set; }
    public ProviderWebhookRemoteStatus? WebhookRemoteStatus { get; private set; }

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
        if (string.IsNullOrWhiteSpace(encryptedCredentials))
            throw new ArgumentException("EncryptedCredentials is required.", nameof(encryptedCredentials));
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

    public void Activate()
    {
        Active = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records the non-sensitive webhook configuration in-place. The
    /// webhook secret itself does NOT live in these columns; it must be
    /// preserved in <c>EncryptedCredentials</c> by calling
    /// <c>UpdateCredentials</c> alongside this method (the application
    /// handler is the single point that knows how to round-trip
    /// <c>{ apiKey, webhookSecret }</c> through <c>ICredentialProtector</c>).
    ///
    /// Passing <c>null</c> in any argument clears that field. <c>eventsJson</c>
    /// is expected to be a JSON array produced by the caller (e.g. via
    /// <c>JsonSerializer.Serialize(new []{ "transparent.completed" })</c>).
    /// Invalid JSON is rejected to keep the column trustworthy.
    /// </summary>
    public void ConfigureWebhook(
        string? callbackUrl,
        string? eventsJson,
        ProviderWebhookRemoteStatus remoteStatus)
    {
        if (eventsJson is not null && !IsValidJsonArray(eventsJson))
            throw new ArgumentException(
                "eventsJson must be a valid JSON array of strings.",
                nameof(eventsJson));

        WebhookCallbackUrl = string.IsNullOrWhiteSpace(callbackUrl) ? null : callbackUrl.Trim();
        WebhookEvents = eventsJson;
        WebhookRemoteStatus = remoteStatus;
        WebhookConfiguredAt = DateTime.UtcNow;
        UpdatedAt = WebhookConfiguredAt.Value;
    }

    private static bool IsValidJsonArray(string value)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
