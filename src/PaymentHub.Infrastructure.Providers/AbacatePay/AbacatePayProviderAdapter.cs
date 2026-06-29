using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Services;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

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
/// <c>{ "apiKey": "...", "secret": "...", "webhookSecret": "..." }</c>
/// blob produced by <c>RegisterProviderAccountHandler</c> and protected
/// via <see cref="ICredentialProtector"/>. The adapter unprotects once,
/// extracts <c>apiKey</c> (for outbound calls) and relies on
/// <c>ProcessWebhookEventHandler</c> to extract <c>webhookSecret</c> for
/// inbound calls (HMAC verification). The plaintext is NEVER logged,
/// returned, or persisted.
/// </para>
/// <para>
/// For inbound webhook validation (Slice 2-B) the adapter delegates
/// signature verification to <see cref="IAbacatePayWebhookSignatureVerifier"/>
/// and payload normalization to <see cref="IAbacatePayWebhookNormalizer"/>.
/// Both are pure: no I/O, no exceptions for malformed input.
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
    private readonly IAbacatePayWebhookSignatureVerifier _signatureVerifier;
    private readonly IAbacatePayWebhookNormalizer _normalizer;
    private readonly ILogger<AbacatePayProviderAdapter> _logger;

    public string ProviderCode => "AbacatePay";

    public AbacatePayProviderAdapter(
        IAbacatePayClient client,
        ICredentialProtector protector,
        IAbacatePayWebhookSignatureVerifier signatureVerifier,
        IAbacatePayWebhookNormalizer normalizer,
        ILogger<AbacatePayProviderAdapter> logger)
    {
        _client = client;
        _protector = protector;
        _signatureVerifier = signatureVerifier;
        _normalizer = normalizer;
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

    /// <summary>
    /// Parse + verify an AbacatePay inbound webhook. Order of checks is
    /// strict: secret present → signature present → signature valid →
    /// payload valid. Any failure returns <see cref="ProviderWebhookParseResult.IsValid"/>
    /// = false with a sanitized <c>ErrorMessage</c> — the secret,
    /// signature, and raw body are NEVER included.
    /// </summary>
    public Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. secret must be present (worker layer responsibility). If the
        //    ProviderAccount was not resolved or has no webhookSecret we
        //    refuse to parse — better to dead-letter the event than to
        //    process an unsigned webhook.
        if (string.IsNullOrEmpty(request.WebhookSecret))
        {
            _logger.LogWarning(
                "AbacatePay webhook rejected: webhookSecret is missing for providerCode='{ProviderCode}'.",
                ProviderCode);
            return Task.FromResult(Invalid(
                eventType: "unknown",
                providerEventId: null,
                error: "AbacatePay webhook secret is missing."));
        }

        // 2. signature header must be present (controller should have
        //    failed-fast already, but defense in depth).
        if (string.IsNullOrWhiteSpace(request.Signature))
        {
            _logger.LogWarning(
                "AbacatePay webhook rejected: signature header is missing.");
            return Task.FromResult(Invalid(
                eventType: "unknown",
                providerEventId: null,
                error: "AbacatePay webhook signature is missing."));
        }

        // 3. signature must verify against the secret.
        var signatureFailure = _signatureVerifier.Verify(
            request.RawBody,
            request.Signature,
            request.WebhookSecret);
        if (signatureFailure != AbacatePayWebhookSignatureFailure.None)
        {
            _logger.LogWarning(
                "AbacatePay webhook rejected: signature verification failed category={Category} providerAccountId={ProviderAccountId}.",
                signatureFailure, request.ProviderAccountId);
            return Task.FromResult(Invalid(
                eventType: "unknown",
                providerEventId: null,
                error: $"AbacatePay webhook signature invalid ({signatureFailure})."));
        }

        // 4. payload must parse + map to a supported event.
        var normalized = _normalizer.Normalize(request.RawBody);
        if (!normalized.IsValid)
        {
            _logger.LogWarning(
                "AbacatePay webhook rejected: payload normalization failed reason={Reason} providerAccountId={ProviderAccountId}.",
                normalized.ErrorMessage, request.ProviderAccountId);
            return Task.FromResult(new ProviderWebhookParseResult(
                IsValid: false,
                ProviderEventId: normalized.EventId,
                EventType: normalized.EventType,
                ProviderPaymentId: normalized.ProviderPaymentId,
                ProviderStatus: normalized.ProviderStatus,
                ErrorMessage: normalized.ErrorMessage,
                RawPayloadJson: normalized.RawPayloadJson));
        }

        return Task.FromResult(new ProviderWebhookParseResult(
            IsValid: true,
            ProviderEventId: normalized.EventId,
            EventType: normalized.EventType,
            ProviderPaymentId: normalized.ProviderPaymentId,
            ProviderStatus: normalized.ProviderStatus,
            ErrorMessage: null,
            RawPayloadJson: normalized.RawPayloadJson));
    }

    private static ProviderWebhookParseResult Invalid(
        string eventType,
        string? providerEventId,
        string error) =>
        new(
            IsValid: false,
            ProviderEventId: providerEventId,
            EventType: eventType,
            ProviderPaymentId: null,
            ProviderStatus: null,
            ErrorMessage: error,
            RawPayloadJson: null);

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
