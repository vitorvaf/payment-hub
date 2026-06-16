using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Infrastructure.Providers.Stripe;

public sealed class StripeProviderAdapter : IPaymentProviderAdapter
{
    private readonly ILogger<StripeProviderAdapter> _logger;

    public string ProviderCode => "Stripe";

    public StripeProviderAdapter(ILogger<StripeProviderAdapter> logger)
    {
        _logger = logger;
    }

    public Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Stripe adapter is a structural skeleton. Wire up real HTTP client before enabling.");
        return Task.FromResult(new CreateCheckoutProviderResult(
            Success: false,
            ProviderPaymentId: null,
            CheckoutUrl: null,
            ErrorMessage: "Stripe adapter not yet implemented.",
            RawResponseJson: null));
    }

    public Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.RawBody) ? "{}" : request.RawBody);
            var root = doc.RootElement;
            var eventType = TryGetString(root, "type") ?? "payment.updated";
            var providerEventId = TryGetString(root, "id");
            var objectData = root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj) ? obj : root;
            var providerPaymentId = TryGetString(objectData, "id");
            var providerStatus = TryGetString(objectData, "status") ?? eventType;

            if (string.IsNullOrWhiteSpace(providerPaymentId))
            {
                return Task.FromResult(new ProviderWebhookParseResult(
                    IsValid: false,
                    ProviderEventId: providerEventId,
                    EventType: eventType,
                    ProviderPaymentId: null,
                    ProviderStatus: providerStatus,
                    ErrorMessage: "Missing provider payment id"));
            }

            return Task.FromResult(new ProviderWebhookParseResult(
                IsValid: true,
                ProviderEventId: providerEventId,
                EventType: eventType,
                ProviderPaymentId: providerPaymentId,
                ProviderStatus: providerStatus,
                ErrorMessage: null,
                RawPayloadJson: request.RawBody));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new ProviderWebhookParseResult(
                IsValid: false,
                ProviderEventId: null,
                EventType: "unknown",
                ProviderPaymentId: null,
                ProviderStatus: null,
                ErrorMessage: ex.Message));
        }
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
        }
        return null;
    }
}
