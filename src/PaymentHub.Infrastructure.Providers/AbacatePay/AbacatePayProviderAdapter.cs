using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Services;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// First functional AbacatePay adapter for Checkout Transparente PIX in
/// sandbox/devMode. Unprotects the API key from the
/// <c>CreateCheckoutProviderRequest.ProtectedCredentials</c> blob (populated
/// by <c>CreateCheckoutHandler</c> from <c>ProviderAccount.EncryptedCredentials</c>),
/// builds the transparent PIX request and delegates to
/// <see cref="IAbacatePayClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Credential contract: <c>EncryptedCredentials</c> is the JSON
/// <c>{ "apiKey": "...", "secret": "..." }</c> blob produced by
/// <c>RegisterProviderAccountHandler</c> and protected via
/// <see cref="ICredentialProtector"/>. The adapter unprotects once,
/// extracts <c>apiKey</c>, and immediately discards the plain JSON. The
/// API key is NEVER logged, returned, or persisted.
/// </para>
/// <para>
/// Status mapping is delegated to <see cref="PaymentStatusMapper"/> so the
/// adapter stays consistent with the rest of the pipeline
/// (Fake/Stripe/MercadoPago).
/// </para>
/// </remarks>
public sealed class AbacatePayProviderAdapter : IPaymentProviderAdapter
{
    private readonly IAbacatePayClient _client;
    private readonly ICredentialProtector _protector;
    private readonly ILogger<AbacatePayProviderAdapter> _logger;

    public string ProviderCode => "AbacatePay";

    public AbacatePayProviderAdapter(
        IAbacatePayClient client,
        ICredentialProtector protector,
        ILogger<AbacatePayProviderAdapter> logger)
    {
        _client = client;
        _protector = protector;
        _logger = logger;
    }

    public async Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProtectedCredentials))
        {
            _logger.LogWarning(
                "AbacatePay CreateCheckout rejected: no protected credentials for payment {PaymentId}.",
                request.PaymentId);
            return Failure("AbacatePay requires ProviderAccount with encrypted credentials.");
        }

        string apiKey;
        try
        {
            var plain = _protector.Unprotect(request.ProtectedCredentials);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(plain) ? "{}" : plain);
            apiKey = doc.RootElement.TryGetProperty("apiKey", out var apiKeyProp)
                ? (apiKeyProp.GetString() ?? string.Empty)
                : string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            _logger.LogWarning(
                ex,
                "AbacatePay CreateCheckout rejected: protected credentials blob is not valid JSON for payment {PaymentId}.",
                request.PaymentId);
            return Failure("AbacatePay credentials are not in the expected JSON shape.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "AbacatePay CreateCheckout rejected: credentials missing 'apiKey' for payment {PaymentId}.",
                request.PaymentId);
            return Failure("AbacatePay credentials are missing the 'apiKey' field.");
        }

        var pixRequest = new AbacatePayCreateTransparentPixRequest
        {
            AmountInCents = request.AmountInCents,
            Description = string.IsNullOrWhiteSpace(request.ExternalReference)
                ? $"PaymentHub payment {request.PaymentId}"
                : request.ExternalReference,
            ExpiresInSeconds = 3600,
            Customer = BuildCustomer(request),
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = request.TenantId.ToString(),
                ["applicationId"] = request.ApplicationId.ToString(),
                ["paymentId"] = request.PaymentId.ToString(),
                ["externalReference"] = request.ExternalReference
            }
        };

        AbacatePayCreateTransparentPixResponse pixResponse;
        try
        {
            pixResponse = await _client
                .CreateTransparentPixAsync(pixRequest, apiKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AbacatePayClientException ex)
        {
            _logger.LogWarning(
                "AbacatePay CreateCheckout failed for payment {PaymentId}: category={Category} statusCode={StatusCode} transient={IsTransient}.",
                request.PaymentId, ex.Category, ex.StatusCode, ex.IsTransient);
            return Failure($"AbacatePay error ({ex.Category}).");
        }

        if (string.IsNullOrWhiteSpace(pixResponse.Id))
        {
            _logger.LogWarning(
                "AbacatePay CreateCheckout returned no provider payment id for payment {PaymentId}.",
                request.PaymentId);
            return Failure("AbacatePay response missing provider payment id.");
        }

        var canonicalStatus = PaymentStatusMapper.FromProviderStatus(ProviderCode, pixResponse.Status ?? "pending");

        _logger.LogInformation(
            "AbacatePay CreateCheckout succeeded for payment {PaymentId} providerPaymentId={ProviderPaymentId} status={Status}.",
            request.PaymentId, pixResponse.Id, canonicalStatus);

        var rawResponse = JsonSerializer.Serialize(new
        {
            provider = "abacatepay",
            providerPaymentId = pixResponse.Id,
            status = pixResponse.Status,
            brCode = pixResponse.BrCode,
            brCodeBase64 = pixResponse.BrCodeBase64,
            expiresAt = pixResponse.ExpiresAt,
            devMode = pixResponse.DevMode ?? false
        });

        return new CreateCheckoutProviderResult(
            Success: true,
            ProviderPaymentId: pixResponse.Id,
            // AbacatePay Checkout Transparente PIX is hosted by the consumer
            // via the returned brCode/brCodeBase64, not a hosted checkout URL.
            // The Payment entity stores CheckoutUrl for symmetry with hosted
            // providers; we expose a synthetic URL so the API contract stays
            // untouched in this slice.
            CheckoutUrl: $"abacatepay://pix/{pixResponse.Id}",
            ErrorMessage: null,
            RawResponseJson: rawResponse);
    }

    public Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken)
    {
        // Full webhook HMAC verification + event normalization ships in Slice 2-B.
        // For this slice we keep the existing JSON-shape parsing so the
        // scaffolding contract does not regress while the adapter evolves.
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

    private static AbacatePayCustomerRequest? BuildCustomer(CreateCheckoutProviderRequest request)
    {
        var hasName = !string.IsNullOrWhiteSpace(request.CustomerName);
        var hasEmail = !string.IsNullOrWhiteSpace(request.CustomerEmail);
        if (!hasName && !hasEmail) return null;

        return new AbacatePayCustomerRequest
        {
            Name = hasName ? request.CustomerName : null,
            Email = hasEmail ? request.CustomerEmail : null
        };
    }

    private static CreateCheckoutProviderResult Failure(string message)
        => new(Success: false, ProviderPaymentId: null, CheckoutUrl: null, ErrorMessage: message, RawResponseJson: null);

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