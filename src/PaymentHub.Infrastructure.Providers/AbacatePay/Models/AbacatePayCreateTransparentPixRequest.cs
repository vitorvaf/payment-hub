using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Request payload for <c>POST /transparents/create</c>. Wrapped in a
/// <c>{ "data": { ... } }</c> envelope at serialization time by
/// <see cref="AbacatePayClient"/>.
/// </summary>
public sealed class AbacatePayCreateTransparentPixRequest
{
    /// <summary>
    /// Amount in cents (AbacatePay convention). Required.
    /// </summary>
    [JsonPropertyName("amount")]
    public long AmountInCents { get; init; }

    /// <summary>
    /// Human-readable description (max ~120 chars). Required.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Seconds until the PIX expires. Default 3600 (1 hour). Required.
    /// </summary>
    [JsonPropertyName("expiresIn")]
    public int ExpiresInSeconds { get; init; } = 3600;

    /// <summary>
    /// Customer block. Optional on our side; omitted entirely when null.
    /// </summary>
    [JsonPropertyName("customer")]
    public AbacatePayCustomerRequest? Customer { get; init; }

    /// <summary>
    /// Free-form metadata echoed back by AbacatePay. Adapter uses this to
    /// carry tenantId/applicationId/paymentId for reconciliation.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}