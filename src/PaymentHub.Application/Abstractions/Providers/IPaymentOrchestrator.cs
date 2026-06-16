using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Application.Abstractions.Providers;

public interface IPaymentOrchestrator
{
    Task<CreateCheckoutResponse> CreateCheckoutAsync(
        CreateCheckoutCommand command,
        CancellationToken cancellationToken);

    Task ProcessWebhookAsync(
        ProcessWebhookEventCommand command,
        CancellationToken cancellationToken);
}

public sealed record CreateCheckoutResponse(
    Guid PaymentId,
    string Status,
    string Provider,
    string? CheckoutUrl);

public sealed record CreateCheckoutCommand(
    Guid TenantId,
    Guid ApplicationId,
    string IdempotencyKey,
    string ExternalReference,
    long AmountInCents,
    string Currency,
    string? CustomerEmail,
    string? CustomerName,
    string? SuccessUrl,
    string? CancelUrl,
    string? MetadataJson,
    IReadOnlyList<ProviderCheckoutItem> Items,
    string? RequestedProviderCode);

public sealed record ProcessWebhookEventCommand(
    Guid WebhookEventId);
