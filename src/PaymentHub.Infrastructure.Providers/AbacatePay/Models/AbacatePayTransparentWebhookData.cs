using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// <c>data</c> section of an AbacatePay webhook. Carries the provider
/// payment id, the runtime status observed by AbacatePay and the metadata
/// we set when creating the cob (PIX). Only the fields we actually consume
/// are surfaced; everything else stays in the raw payload.
/// </summary>
public sealed class AbacatePayTransparentWebhookData
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("amount")]
    public long? AmountInCents { get; init; }

    [JsonPropertyName("devMode")]
    public bool? DevMode { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string?>? Metadata { get; init; }
}
