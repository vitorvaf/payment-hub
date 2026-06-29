using PaymentHub.Application.Abstractions.Security;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// In-memory test double for <see cref="IWebhookSecretProtector"/>.
/// It performs a reversible XOR encoding with a marker prefix so unit tests
/// can assert that secrets were passed through the protector and that the raw
/// value never reaches the persistence layer. NOT cryptographic — test only.
/// </summary>
public sealed class FakeWebhookSecretProtector : IWebhookSecretProtector
{
    public const string Marker = "fake-protect|";

    public string Protect(string plainTextSecret)
    {
        if (plainTextSecret is null) throw new ArgumentNullException(nameof(plainTextSecret));
        if (string.IsNullOrEmpty(plainTextSecret))
            throw new ArgumentException("Webhook secret cannot be empty.", nameof(plainTextSecret));
        return Marker + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainTextSecret));
    }

    public string Unprotect(string protectedSecret)
    {
        if (protectedSecret is null) throw new ArgumentNullException(nameof(protectedSecret));
        if (!protectedSecret.StartsWith(Marker, StringComparison.Ordinal))
            throw new InvalidOperationException("FakeWebhookSecretProtector: missing expected marker.");
        var encoded = protectedSecret[Marker.Length..];
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }
}