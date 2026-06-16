using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Infrastructure.Providers.Fake;

public sealed class FakePaymentProviderAdapter : IPaymentProviderAdapter
{
    private readonly ILogger<FakePaymentProviderAdapter> _logger;

    public string ProviderCode => "Fake";

    public FakePaymentProviderAdapter(ILogger<FakePaymentProviderAdapter> logger)
    {
        _logger = logger;
    }

    public Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken)
    {
        var checkoutUrl = $"https://fake-checkout.local/payments/{request.PaymentId}";
        var providerPaymentId = $"fake_{request.PaymentId:N}";
        var rawResponse = JsonSerializer.Serialize(new
        {
            id = providerPaymentId,
            checkoutUrl,
            status = "pending"
        });

        _logger.LogInformation(
            "Fake provider created checkout for payment {PaymentId} with providerId {ProviderPaymentId}",
            request.PaymentId, providerPaymentId);

        return Task.FromResult(new CreateCheckoutProviderResult(
            Success: true,
            ProviderPaymentId: providerPaymentId,
            CheckoutUrl: checkoutUrl,
            ErrorMessage: null,
            RawResponseJson: rawResponse));
    }

    public Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.RawBody) ? "{}" : request.RawBody);
            var root = doc.RootElement;

            var providerPaymentId = TryGetString(root, "providerPaymentId", "id", "paymentId");
            var eventType = TryGetString(root, "eventType", "type") ?? "payment.updated";
            var providerStatus = TryGetString(root, "status") ?? eventType;
            var providerEventId = TryGetString(root, "eventId", "id");

            if (string.IsNullOrWhiteSpace(providerPaymentId))
            {
                return Task.FromResult(new ProviderWebhookParseResult(
                    IsValid: false,
                    ProviderEventId: null,
                    EventType: eventType,
                    ProviderPaymentId: null,
                    ProviderStatus: null,
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

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
                if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
            }
        }
        return null;
    }
}
