using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Tenants.Dtos;

public sealed record RegisterTenantRequestDto(string Name, string Slug);

public sealed record TenantResponseDto(Guid Id, string Name, string Slug, string Status, DateTime CreatedAt);

public sealed record RegisterApplicationClientRequestDto(
    Guid TenantId,
    string Name,
    string? WebhookUrl,
    string? WebhookSecret,
    ProviderCode? DefaultProvider);

public sealed record ApplicationClientResponseDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? WebhookUrl,
    bool HasWebhookSecret,
    ProviderCode? DefaultProvider,
    string Status,
    string? ApiKey);

public sealed record RegisterProviderAccountRequestDto(
    ProviderCode ProviderCode,
    ProviderEnvironment Environment,
    string Name,
    string ApiKey,
    string? Secret,
    bool IsDefault);

public sealed record ProviderAccountResponseDto(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    ProviderCode ProviderCode,
    ProviderEnvironment Environment,
    string Name,
    bool IsDefault,
    bool Active,
    DateTime CreatedAt);

// ----- Slice 2-C: AbacatePay webhook management -----

/// <summary>
/// Body accepted by <c>PUT /api/v1/provider-accounts/{providerAccountId}/webhook</c>.
///
/// SECURITY: the body MUST NOT contain <c>webhookSecret</c> in clear text.
/// The protected blob is updated by <c>EncryptedCredentials</c> only.
/// <c>webhookSecret</c> lives inside <c>ProviderAccount.EncryptedCredentials</c>
/// as JSON, and is protected by <c>ICredentialProtector</c>. The handler
/// unpacks the existing credentials, preserves <c>apiKey</c>, optionally
/// updates <c>webhookSecret</c> when supplied, and re-protects.
/// </summary>
public sealed record ConfigureAbacatePayWebhookRequestDto(
    string? CallbackUrl,
    IReadOnlyList<string>? Events,
    string? WebhookSecret,
    bool RegisterRemotely = false);

/// <summary>
/// Body returned by both <c>PUT</c> and <c>GET</c> webhook endpoints.
/// Intentionally safe: it never exposes <c>apiKey</c>, raw <c>webhookSecret</c>,
/// protected <c>webhookSecret</c> blob or <c>EncryptedCredentials</c>.
/// </summary>
public sealed record ProviderAccountWebhookResponseDto(
    Guid ProviderAccountId,
    ProviderCode ProviderCode,
    ProviderEnvironment Environment,
    string? CallbackUrl,
    IReadOnlyList<string> Events,
    bool HasWebhookSecret,
    string? RemoteRegistrationStatus,
    DateTime? ConfiguredAt,
    DateTime? UpdatedAt);
