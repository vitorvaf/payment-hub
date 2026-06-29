using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Response payload for <c>POST /transparents/simulate-payment</c>. AbacatePay
/// mirrors the standard check response with the status advanced to <c>PAID</c>.
/// </summary>
public sealed class AbacatePaySimulatePaymentResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}