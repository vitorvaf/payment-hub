using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Abstractions.Observability;
using PaymentHub.Application.Observability;
using PaymentHub.Application.Webhooks;
using PaymentHub.Infrastructure.Providers.AbacatePay;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/webhooks")]
public class ProviderWebhooksController : ControllerBase
{
    private readonly IReceiveProviderWebhookHandler _handler;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly ILogger<ProviderWebhooksController> _logger;

    public ProviderWebhooksController(
        IReceiveProviderWebhookHandler handler,
        ICorrelationIdAccessor correlationIdAccessor,
        ILogger<ProviderWebhooksController> logger)
    {
        _handler = handler;
        _correlationIdAccessor = correlationIdAccessor;
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
        // Slice 9-O2: provider webhooks received counter increments on every
        // accepted request (the controller edge is the entrypoint for
        // inbound provider webhooks).
        PaymentHubMetrics.ProviderWebhooksReceivedTotal.Record(1,
            PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.Provider, providerCode));

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
            // Slice 9-O2: rejection counter + safe category tag (never the
            // signature value).
            PaymentHubMetrics.ProviderWebhooksRejectedTotal.Record(1,
                PaymentHubMetrics.Tag(
                    PaymentHubMetrics.TagKeys.Provider, providerCode,
                    PaymentHubMetrics.TagKeys.ErrorCategory, "missing_signature"));
            _logger.LogWarning(
                "{Event} provider={ProviderCode} reason={Reason} path={Path}",
                PaymentHubLogEvents.ProviderWebhookRejected, providerCode, "missing_signature", Request.Path);
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
            catch (JsonException)
            {
                // Slice 9-O2: invalid JSON -> rejection counter.
                PaymentHubMetrics.ProviderWebhooksRejectedTotal.Record(1,
                    PaymentHubMetrics.Tag(
                        PaymentHubMetrics.TagKeys.Provider, providerCode,
                        PaymentHubMetrics.TagKeys.ErrorCategory, "invalid_json"));
                _logger.LogWarning(
                    "{Event} provider={ProviderCode} reason={Reason}",
                    PaymentHubLogEvents.ProviderWebhookInvalidJson, providerCode, "invalid_json");
                return BadRequest(new { error = "invalid_json" });
            }
        }

        if (string.IsNullOrWhiteSpace(eventType))
            eventType = "payment.updated";

        // Slice 9-O1.2: capture the resolved correlation id so the inbox
        // processor (and any downstream outbox row it creates) carries the
        // same value end-to-end. The middleware already populated the
        // accessor and pushed the value onto the Serilog LogContext.
        var correlationId = _correlationIdAccessor.CorrelationId;

        try
        {
            var webhookId = await _handler.HandleAsync(
                providerCode, eventType!, rawBody, providerEventId, signature, correlationId, cancellationToken);
            // Slice 9-O2: structured log of accepted webhooks. SafeLog.Length
            // reports the body size without surfacing the payload content.
            _logger.LogInformation(
                "{Event} provider={ProviderCode} eventTypeLength={EventTypeLength} webhookId={WebhookId}",
                PaymentHubLogEvents.ProviderWebhookReceived, providerCode, SafeLog.Length(eventType),
                SafeLog.Id(webhookId));
            return Accepted(new { webhookId });
        }
        catch (InvalidOperationException ex)
        {
            // Slice 9-O2: persist failure -> rejection counter.
            PaymentHubMetrics.ProviderWebhooksRejectedTotal.Record(1,
                PaymentHubMetrics.Tag(
                    PaymentHubMetrics.TagKeys.Provider, providerCode,
                    PaymentHubMetrics.TagKeys.ErrorCategory, "persist_failed"));
            _logger.LogWarning(
                "{Event} provider={ProviderCode} reason={Reason} messageLength={MessageLength}",
                PaymentHubLogEvents.ProviderWebhookRejected, providerCode, "persist_failed", SafeLog.Length(ex.Message));
            return UnprocessableEntity(new { error = "webhook_persist_failed", message = ex.Message });
        }
    }

    private static bool IsAbacatePay(string providerCode)
        => string.Equals(providerCode, "AbacatePay", StringComparison.OrdinalIgnoreCase);
}

