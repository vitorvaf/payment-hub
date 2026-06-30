using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

/// <summary>
/// Turns a raw AbacatePay v2 webhook payload into a
/// <see cref="AbacatePayWebhookNormalizationResult"/> carrying the canonical
/// fields the Payment Hub pipeline needs. Pure: no I/O, no logging, no
/// exceptions for malformed / unknown input.
/// </summary>
public interface IAbacatePayWebhookNormalizer
{
    AbacatePayWebhookNormalizationResult Normalize(string rawBody);
}
