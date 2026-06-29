namespace PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

/// <summary>
/// Result of validating an AbacatePay webhook signature. Tells the caller
/// whether the request can be trusted and, when it cannot, why — without
/// leaking the secret in the reason text.
/// </summary>
/// <remarks>
/// <para>
/// Slice 2-B contract: the verifier NEVER persists or logs the
/// <c>webhookSecret</c>. It also does not surface the secret, the raw
/// signature, or the protected credential blob in any error message.
/// </para>
/// <para>
/// <see cref="RequiresSecret"/> means the verifier cannot decide without a
/// key (e.g. signature header is missing). The caller (controller or
/// adapter) maps this to HTTP 401.
/// </para>
/// </remarks>
public enum AbacatePayWebhookSignatureFailure
{
    None = 0,

    /// <summary>Signature is valid (no failure).</summary>
    NoneValid = 0,

    /// <summary>No <c>X-Webhook-Signature</c> header on the request.</summary>
    MissingSignature = 1,

    /// <summary>The signature header could not be base64 decoded.</summary>
    MalformedSignature = 2,

    /// <summary>No <c>webhookSecret</c> could be resolved for the provider account.</summary>
    MissingSecret = 3,

    /// <summary>The signature does not match the HMAC-SHA256(payload, secret).</summary>
    SignatureMismatch = 4
}
