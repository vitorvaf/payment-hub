namespace PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

/// <summary>
/// Verifies the <c>X-Webhook-Signature</c> header sent by AbacatePay on
/// inbound webhooks. Signature algorithm is HMAC-SHA256 over the raw UTF-8
/// body, encoded as Base64. Same shape as the Stripe-style verification path,
/// adapted to the AbacatePay docs.
/// </summary>
/// <remarks>
/// <para>
/// The verifier is intentionally pure: it takes the raw body, the signature
/// header value (already extracted from the HTTP request) and the
/// <c>webhookSecret</c> stored in the provider account. It does not read
/// headers, does not parse the body, and does not touch the database.
/// </para>
/// <para>
/// All comparisons use <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>
/// to avoid timing oracles. The <c>webhookSecret</c> parameter is never
/// included in any error message exposed by <see cref="Verify"/>.
/// </para>
/// </remarks>
public interface IAbacatePayWebhookSignatureVerifier
{
    /// <summary>
    /// Result category. <see cref="AbacatePayWebhookSignatureFailure.None"/> means
    /// the signature is valid. Any other value tells the caller which
    /// contract was violated; an explicit message is intentionally NOT
    /// exposed so logs and HTTP responses do not leak which guardrail
    /// triggered.
    /// </summary>
    AbacatePayWebhookSignatureFailure Verify(
        string rawBody,
        string? signatureHeader,
        string? webhookSecret);
}
