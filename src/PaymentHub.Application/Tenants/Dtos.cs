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
