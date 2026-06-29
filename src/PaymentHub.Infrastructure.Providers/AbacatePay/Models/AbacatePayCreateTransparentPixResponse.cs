using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Response payload for <c>POST /transparents/create</c>. The PIX copy-paste
/// code and base64 PNG are stored on the request DTO and round-tripped in
/// <c>RawResponseJson</c> for callers that need them.
/// </summary>
public sealed class AbacatePayCreateTransparentPixResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("amount")]
    public long? AmountInCents { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("brCode")]
    public string? BrCode { get; init; }

    [JsonPropertyName("brCodeBase64")]
    public string? BrCodeBase64 { get; init; }

    [JsonPropertyName("devMode")]
    public bool? DevMode { get; init; }
}