using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Application.Abstractions.Providers;

public interface IPaymentProviderAdapter
{
    string ProviderCode { get; }

    Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken);

    Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken);
}

public interface IPaymentProviderRouter
{
    IPaymentProviderAdapter Resolve(string? requestedProviderCode);
}
