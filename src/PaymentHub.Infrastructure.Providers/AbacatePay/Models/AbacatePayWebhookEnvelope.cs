using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Webhook envelope shape for AbacatePay v2. Follows the canonical contract
/// documented at <c>https://docs.abacatepay.com/pages/webhooks</c>:
/// <c>{ id, event, apiVersion, devMode, data }</c>. Only the fields we need
/// are mapped; other top-level keys are ignored without breaking the parse.
/// </summary>
public sealed class AbacatePayWebhookEnvelope
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("apiVersion")]
    public int? ApiVersion { get; init; }

    [JsonPropertyName("devMode")]
    public bool? DevMode { get; init; }

    [JsonPropertyName("data")]
    public AbacatePayTransparentWebhookData? Data { get; init; }
}
