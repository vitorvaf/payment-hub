using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Infrastructure.Providers.MercadoPago;

public sealed class MercadoPagoProviderAdapter : IPaymentProviderAdapter
{
    private readonly ILogger<MercadoPagoProviderAdapter> _logger;

    public string ProviderCode => "MercadoPago";

    public MercadoPagoProviderAdapter(ILogger<MercadoPagoProviderAdapter> logger)
    {
        _logger = logger;
    }

    public Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("MercadoPago adapter is a structural skeleton. Wire up real HTTP client before enabling.");
        return Task.FromResult(new CreateCheckoutProviderResult(
            Success: false,
            ProviderPaymentId: null,
            CheckoutUrl: null,
            ErrorMessage: "MercadoPago adapter not yet implemented.",
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
            var eventType = TryGetString(root, "type") ?? TryGetString(root, "action") ?? "payment.updated";
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var providerPaymentId = TryGetString(data, "id");
            var providerStatus = TryGetString(root, "status") ?? eventType;
            var providerEventId = TryGetString(root, "id");

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
