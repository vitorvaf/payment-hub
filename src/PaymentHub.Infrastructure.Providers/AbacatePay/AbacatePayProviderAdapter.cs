using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

public sealed class AbacatePayProviderAdapter : IPaymentProviderAdapter
{
    private readonly ILogger<AbacatePayProviderAdapter> _logger;

    public string ProviderCode => "AbacatePay";

    public AbacatePayProviderAdapter(ILogger<AbacatePayProviderAdapter> logger)
    {
        _logger = logger;
    }

    public Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "AbacatePay adapter is a structural skeleton. Configure credentials and HTTP client before enabling in production.");

        return Task.FromResult(new CreateCheckoutProviderResult(
            Success: false,
            ProviderPaymentId: null,
            CheckoutUrl: null,
            ErrorMessage: "AbacatePay adapter not yet implemented.",
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

            var providerPaymentId = TryGetString(root, "data", "id") ?? TryGetString(root, "id");
            var eventType = TryGetString(root, "event") ?? "payment.updated";
            var providerStatus = TryGetString(root, "data", "status") ?? eventType;
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

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        JsonElement current = element;
        for (int i = 0; i < names.Length; i++)
        {
            if (!current.TryGetProperty(names[i], out var next)) return null;
            if (i == names.Length - 1)
            {
                return next.ValueKind == JsonValueKind.String ? next.GetString() : next.GetRawText();
            }
            current = next;
        }
        return null;
    }
}
