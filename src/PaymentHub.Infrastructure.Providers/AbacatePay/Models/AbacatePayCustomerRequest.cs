using System.Text.Json.Serialization;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Models;

/// <summary>
/// Customer block sent to <c>POST /transparents/create</c>. All fields are
/// optional on our side; the adapter omits the block when missing required
/// data to avoid the AbacatePay 400 <c>customer.* required</c> response.
/// </summary>
public sealed class AbacatePayCustomerRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("taxId")]
    public string? TaxId { get; init; }

    [JsonPropertyName("cellphone")]
    public string? Cellphone { get; init; }
}