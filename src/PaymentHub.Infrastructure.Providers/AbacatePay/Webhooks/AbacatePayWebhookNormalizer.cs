using System.Text.Json;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

/// <summary>
/// Default AbacatePay webhook normalizer. Json-parses the body into
/// <see cref="AbacatePayWebhookEnvelope"/>, extracts the canonical fields
/// (<c>eventId</c>, <c>eventType</c>, <c>providerPaymentId</c>, etc.) and
/// maps <c>transparent.*</c> events to <see cref="PaymentStatus"/> via
/// <see cref="PaymentStatusMapper"/>.
/// </summary>
/// <remarks>
/// <para>
/// The normalizer DOES NOT throw on bad JSON or unknown events — the
/// adapter translates the <see cref="AbacatePayWebhookNormalizationResult"/>
/// into the existing <see cref="PaymentHub.Application.Abstractions.Providers.ProviderWebhookParseResult"/>
/// contract used by the rest of the pipeline.
/// </para>
/// <para>
/// Status mapping decisions are documented in unit tests so future
/// agents changing <see cref="PaymentStatusMapper"/> can see the
/// reasoning.
/// </para>
/// </remarks>
public sealed class AbacatePayWebhookNormalizer : IAbacatePayWebhookNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Supported v2 events for the Checkout Transparente PIX surface. Other
    /// events (<c>checkout.*</c>, <c>subscription.*</c>, <c>payout.*</c>,
    /// <c>transfer.*</c>, <c>transparent.expired</c>) are intentionally not
    /// handled here — they belong to other providers/products.
    /// </summary>
    private static readonly HashSet<string> SupportedEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "transparent.completed",
        "transparent.refunded",
        "transparent.disputed",
        "transparent.lost"
    };

    public AbacatePayWebhookNormalizationResult Normalize(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return AbacatePayWebhookNormalizationResult.Invalid("Empty AbacatePay webhook payload.");
        }

        AbacatePayWebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<AbacatePayWebhookEnvelope>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AbacatePayWebhookNormalizationResult.Invalid(
                $"AbacatePay webhook JSON could not be parsed: {ex.Message}");
        }

        if (envelope is null)
        {
            return AbacatePayWebhookNormalizationResult.Invalid("AbacatePay webhook envelope is null.");
        }

        var eventType = (envelope.Event ?? string.Empty).Trim();
        if (eventType.Length == 0)
        {
            return AbacatePayWebhookNormalizationResult.Invalid("AbacatePay webhook event is missing.");
        }

        if (!SupportedEvents.Contains(eventType))
        {
            return AbacatePayWebhookNormalizationResult.Invalid(
                $"Unsupported AbacatePay event '{eventType}'.");
        }

        var eventId = string.IsNullOrWhiteSpace(envelope.Id) ? null : envelope.Id.Trim();
        if (string.IsNullOrEmpty(eventId))
        {
            // eventId is the Idempotency anchor; without it the pipeline
            // cannot guarantee dedup on retries.
            return AbacatePayWebhookNormalizationResult.Invalid(
                "AbacatePay webhook id is missing.");
        }

        var providerPaymentId = envelope.Data?.Id?.Trim();
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return AbacatePayWebhookNormalizationResult.Invalid(
                "AbacatePay webhook data.id (provider payment id) is missing.");
        }

        var providerStatus = envelope.Data?.Status?.Trim();
        if (string.IsNullOrWhiteSpace(providerStatus))
        {
            return AbacatePayWebhookNormalizationResult.Invalid(
                "AbacatePay webhook data.status is missing.");
        }

        return new AbacatePayWebhookNormalizationResult(
            IsValid: true,
            EventId: eventId,
            EventType: eventType,
            ProviderPaymentId: providerPaymentId,
            ProviderStatus: providerStatus,
            ErrorMessage: null,
            RawPayloadJson: rawBody);
    }

    /// <summary>
    /// Maps an AbacatePay event name to a canonical
    /// <see cref="PaymentStatus"/>. Decisions documented inline next to
    /// each branch — mirrors <see cref="PaymentStatusMapper"/> but keeps
    /// event-name semantics separate from raw status semantics.
    /// </summary>
    public static PaymentStatus MapEvent(string eventType, string providerStatus)
    {
        var normalizedEvent = (eventType ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedStatus = (providerStatus ?? string.Empty).Trim().ToLowerInvariant();

        return normalizedEvent switch
        {
            // transparent.completed: PAID/APPROVED => Approved, anything
            // else (PENDING) sticks at Pending until further confirmation.
            "transparent.completed" when normalizedStatus is "paid" or "approved" => PaymentStatus.Approved,
            "transparent.completed" => PaymentStatus.Pending,

            // transparent.refunded: canonical refund outcome.
            "transparent.refunded" => PaymentStatus.Refunded,

            // transparent.disputed: chargeback opened. MVP does not run
            // its own dispute flow, so we keep payment Pending until the
            // dispute is won or lost.
            "transparent.disputed" => PaymentStatus.Pending,

            // transparent.lost: dispute lost by the merchant. Treated as
            // Failed because the funds were definitively reversed.
            "transparent.lost" => PaymentStatus.Failed,

            _ => PaymentStatus.Pending
        };
    }
}
