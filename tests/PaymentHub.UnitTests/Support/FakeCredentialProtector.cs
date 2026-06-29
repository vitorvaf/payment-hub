using PaymentHub.Application.Abstractions.Security;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// In-memory test double for <see cref="ICredentialProtector"/>. Mirrors the
/// pattern of <see cref="FakeWebhookSecretProtector"/>: a marker-prefixed
/// reversible encoding so tests can assert that the adapter calls
/// <c>Protect</c>/<c>Unprotect</c> without dragging in
/// <c>AesCredentialProtector</c>'s constructor that requires
/// <c>PaymentHub:CredentialEncryptionKey</c>. NOT cryptographic — test only.
/// </summary>
public sealed class FakeCredentialProtector : ICredentialProtector
{
    public const string Marker = "fake-cred|";

    public string Protect(string plainText)
    {
        if (plainText is null) throw new ArgumentNullException(nameof(plainText));
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Plaintext credential cannot be empty.", nameof(plainText));
        return Marker + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));
    }

    public string Unprotect(string protectedText)
    {
        if (protectedText is null) throw new ArgumentNullException(nameof(protectedText));
        if (!protectedText.StartsWith(Marker, StringComparison.Ordinal))
            throw new InvalidOperationException("FakeCredentialProtector: missing expected marker.");
        var encoded = protectedText[Marker.Length..];
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }
}