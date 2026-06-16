using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Payments.Dtos;

public sealed record PaymentResponseDto(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    string ExternalReference,
    long Amount,
    string Currency,
    ProviderCode Provider,
    PaymentStatus Status,
    string? ProviderPaymentId,
    string? CheckoutUrl,
    string? CustomerEmail,
    string? CustomerName,
    DateTime CreatedAt,
    DateTime? ProcessedAt);

public sealed record PaymentListItemDto(
    Guid Id,
    string ExternalReference,
    long Amount,
    string Currency,
    ProviderCode Provider,
    PaymentStatus Status,
    DateTime CreatedAt);
