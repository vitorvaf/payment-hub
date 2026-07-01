using PaymentHub.Application.Abstractions.Security;

namespace PaymentHub.Application.Abstractions.Security;

/// <summary>
/// Public reader over <c>ProviderAccount.EncryptedCredentials</c>
/// blobs. Slice 2-C exposed the implementation as
/// <c>internal static</c> (<see cref="ProviderAccountCredentialsInspector"/>)
/// because its only caller was the configure-handler. Slice 2-C.1
/// needs the same helper from the Infrastructure layer (the
/// <c>AbacatePayWebhookManagementClient</c>), so the apiKey extraction
/// method is promoted to this public interface while keeping the
/// zero-allocation, no-exception guarantees.
/// </summary>
/// <remarks>
/// <para>
/// Returning <c>null</c> for any failure mode (unprotectable blob,
/// malformed JSON, missing field) keeps callers from leaking the
/// provenance through exception types. Bad input always means bad
/// outcome; good input still requires the caller to validate
/// downstream.
/// </para>
/// </remarks>
public interface IProviderAccountCredentialsReader
{
    /// <summary>
    /// Best-effort read of <c>apiKey</c> from
    /// <c>ProviderAccount.EncryptedCredentials</c>. Returns
    /// <c>null</c> on any failure (unprotectable blob,
    /// non-JSON payload, missing field, whitespace value).
    /// Never throws.
    /// </summary>
    string? ReadApiKey(string encryptedCredentials);
}
