using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Abstractions.Providers;

public sealed record CreateCheckoutProviderRequest(
    Guid TenantId,
    Guid ApplicationId,
    Guid PaymentId,
    string ExternalReference,
    long AmountInCents,
    string Currency,
    string? CustomerEmail,
    string? CustomerName,
    string? SuccessUrl,
    string? CancelUrl,
    string? MetadataJson,
    IReadOnlyList<ProviderCheckoutItem> Items);

public sealed record ProviderCheckoutItem(
    string Id,
    string Name,
    int Quantity,
    long UnitAmount);

public sealed record CreateCheckoutProviderResult(
    bool Success,
    string? ProviderPaymentId,
    string? CheckoutUrl,
    string? ErrorMessage,
    string? RawResponseJson = null);

public sealed record ProviderWebhookRequest(
    string RawBody,
    string? Signature,
    IReadOnlyDictionary<string, string> Headers);

public sealed record ProviderWebhookParseResult(
    bool IsValid,
    string? ProviderEventId,
    string EventType,
    string? ProviderPaymentId,
    string? ProviderStatus,
    string? ErrorMessage,
    string? RawPayloadJson = null);
