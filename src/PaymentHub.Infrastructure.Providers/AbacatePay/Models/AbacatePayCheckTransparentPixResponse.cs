using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Response payload for <c>GET /transparents/check?id=...</c>. Returns the
/// current AbacatePay-side status of a PIX charge. Used by the adapter's
/// internal status sync path. Not exposed on <c>IPaymentProviderAdapter</c>
/// in this slice — kept concrete for future integration.
/// </summary>
public sealed class AbacatePayCheckTransparentPixResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("amount")]
    public long? AmountInCents { get; init; }

    [JsonPropertyName("paidAt")]
    public DateTimeOffset? PaidAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}