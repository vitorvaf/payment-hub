using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Webhooks;
using PaymentHub.Infrastructure.Providers.AbacatePay;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/webhooks")]
public class ProviderWebhooksController : ControllerBase
{
    private readonly IReceiveProviderWebhookHandler _handler;
    private readonly ILogger<ProviderWebhooksController> _logger;

    public ProviderWebhooksController(
        IReceiveProviderWebhookHandler handler,
        ILogger<ProviderWebhooksController> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [HttpPost("{providerCode}")]
    public async Task<IActionResult> Receive(
        string providerCode,
        [FromHeader(Name = "X-Provider-Event-Id")] string? providerEventId,
        [FromHeader(Name = "X-Provider-Event-Type")] string? eventType,
        // Slice 2-B: AbacatePay uses X-Webhook-Signature. Providers that
        // pre-dated this contract can keep sending X-Provider-Signature.
        // Whichever header arrives first wins — we never trust both.
        [FromHeader(Name = "X-Provider-Signature")] string? legacySignature,
        [FromHeader(Name = HmacAbacatePayWebhookSignatureVerifier.SignatureHeaderName)] string? abacateSignature,
        CancellationToken cancellationToken)
    {
        var signature = !string.IsNullOrWhiteSpace(abacateSignature)
            ? abacateSignature
            : legacySignature;

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        // Slice 2-B: AbacatePay REQUIRES HMAC for every event. Reject the
        // request before any DB write when the signature header is missing.
        // For other providers we keep the legacy permissive behavior.
        if (IsAbacatePay(providerCode) && string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning(
                "Rejected inbound AbacatePay webhook without signature header on path {Path}.",
                Request.Path);
            return Unauthorized(new { error = "missing_signature" });
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
                if (doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    eventType = t.GetString();
                else if (doc.RootElement.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String)
                    eventType = e.GetString();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON payload for provider webhook {ProviderCode}", providerCode);
                return BadRequest(new { error = "invalid_json" });
            }
        }

        if (string.IsNullOrWhiteSpace(eventType))
            eventType = "payment.updated";

        try
        {
            var webhookId = await _handler.HandleAsync(
                providerCode, eventType!, rawBody, providerEventId, signature, cancellationToken);
            return Accepted(new { webhookId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to persist provider webhook {ProviderCode}", providerCode);
            return UnprocessableEntity(new { error = "webhook_persist_failed", message = ex.Message });
        }
    }

    private static bool IsAbacatePay(string providerCode)
        => string.Equals(providerCode, "AbacatePay", StringComparison.OrdinalIgnoreCase);
}

