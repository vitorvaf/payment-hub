using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Standard envelope returned by AbacatePay REST endpoints. The provider always
/// returns <c>{ data, success, error? }</c>; <see cref="AbacatePayClient"/> validates
/// <c>success=true</c> before returning the payload and raises
/// <c>AbacatePayClientException(EnvelopeFailure)</c> otherwise.
/// </summary>
/// <typeparam name="T">Type of the inner payload. Use <c>object</c> for endpoints we don't model.</typeparam>
public sealed class AbacatePayEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}