namespace PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

/// <summary>
/// Outcome of normalizing an AbacatePay webhook payload into the Payment Hub
/// canonical contract (<see cref="PaymentHub.Domain.Enums.PaymentStatus"/>).
/// The normalizer returns a value object that the adapter hands back to the
/// webhook pipeline — no exception is thrown for "unknown event" or
/// "missing fields" because that is part of the normalizer's job to detect
/// and surface as <see cref="AbacatePayWebhookNormalizationResult.IsValid"/>.
/// </summary>
public sealed record AbacatePayWebhookNormalizationResult(
    bool IsValid,
    string? EventId,
    string EventType,
    string? ProviderPaymentId,
    string? ProviderStatus,
    string? ErrorMessage,
    string? RawPayloadJson)
{
    /// <summary>
    /// Convenience helper for "unknown event" cases. Keeps callers from
    /// having to fill every field manually.
    /// </summary>
    public static AbacatePayWebhookNormalizationResult Invalid(string errorMessage) =>
        new(
            IsValid: false,
            EventId: null,
            EventType: "unknown",
            ProviderPaymentId: null,
            ProviderStatus: null,
            ErrorMessage: errorMessage,
            RawPayloadJson: null);
}
