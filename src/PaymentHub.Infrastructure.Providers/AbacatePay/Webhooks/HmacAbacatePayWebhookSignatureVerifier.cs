using System.Security.Cryptography;
using System.Text;

namespace PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

/// <summary>
/// Default <see cref="IAbacatePayWebhookSignatureVerifier"/> using
/// HMAC-SHA256 over the raw UTF-8 request body, with the resulting digest
/// compared (in constant time) against the Base64-decoded
/// <c>X-Webhook-Signature</c> header value.
/// </summary>
/// <remarks>
/// <para>
/// Algorithm follows
/// <c>https://docs.abacatepay.com/pages/webhooks</c>: the secret is treated
/// as a shared symmetric key; the body is exactly as received from the
/// HTTP request, without reserialization; the signature header is Base64
/// (the docs example uses Base64).
/// </para>
/// <para>
/// The implementation never logs or returns the secret, the signature, or
/// the protected credential blob. Error categories (see
/// <see cref="AbacatePayWebhookSignatureFailure"/>) are the only thing the
/// caller gets back.
/// </para>
/// </remarks>
public sealed class HmacAbacatePayWebhookSignatureVerifier : IAbacatePayWebhookSignatureVerifier
{
    /// <summary>
    /// Header name as documented by AbacatePay. Matched exactly by the
    /// controller when extracting the signature.
    /// </summary>
    public const string SignatureHeaderName = "X-Webhook-Signature";

    public AbacatePayWebhookSignatureFailure Verify(
        string rawBody,
        string? signatureHeader,
        string? webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return AbacatePayWebhookSignatureFailure.MissingSignature;

        if (string.IsNullOrWhiteSpace(webhookSecret))
            return AbacatePayWebhookSignatureFailure.MissingSecret;

        // Body must be the exact bytes the provider sent. Caller is
        // expected to pass the raw UTF-8 view; null is treated as empty.
        var body = rawBody ?? string.Empty;

        byte[] provided;
        try
        {
            provided = Convert.FromBase64String(signatureHeader);
        }
        catch (FormatException)
        {
            return AbacatePayWebhookSignatureFailure.MalformedSignature;
        }

        byte[] expected;
        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        }
        catch (EncoderFallbackException)
        {
            // webhooks encoded with characters the UTF-8 encoder cannot
            // represent — treat as missing secret (defensive).
            return AbacatePayWebhookSignatureFailure.MissingSecret;
        }

        if (provided.Length != expected.Length)
            return AbacatePayWebhookSignatureFailure.SignatureMismatch;

        return CryptographicOperations.FixedTimeEquals(provided, expected)
            ? AbacatePayWebhookSignatureFailure.None
            : AbacatePayWebhookSignatureFailure.SignatureMismatch;
    }
}
