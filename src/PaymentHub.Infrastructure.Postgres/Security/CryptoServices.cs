using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Infrastructure.Postgres.Options;

namespace PaymentHub.Infrastructure.Postgres.Security;

public sealed class HmacApiKeyHasher : IApiKeyHasher
{
    private readonly byte[] _secret;

    public HmacApiKeyHasher(IOptions<PaymentHubOptions> options)
    {
        var secret = options.Value.ApiKeyHashSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("PaymentHub:ApiKeyHashSecret is required.");
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string Hash(string apiKey)
    {
        using var hmac = new HMACSHA256(_secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool Verify(string apiKey, string hash)
    {
        var computed = Hash(apiKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(hash));
    }
}

public sealed class AesCredentialProtector : ICredentialProtector
{
    private readonly byte[] _key;

    public AesCredentialProtector(IOptions<PaymentHubOptions> options)
    {
        var raw = options.Value.CredentialEncryptionKey;
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("PaymentHub:CredentialEncryptionKey is required.");
        if (raw.Length < 32) raw = raw.PadRight(32, '0');
        _key = Encoding.UTF8.GetBytes(raw.Substring(0, 32));
    }

    public string Protect(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }

    public string Unprotect(string protectedText)
    {
        var data = Convert.FromBase64String(protectedText);
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 16, cipherBytes, 0, cipherBytes.Length);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

public sealed class HmacWebhookSigner : IWebhookSigner
{
    public string Sign(string payload, string secret)
        => Sign(payload, secret, string.Empty);

    public string Sign(string payload, string secret, string timestamp)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        var signedPayload = string.IsNullOrEmpty(timestamp)
            ? payload
            : $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool Verify(string payload, string secret, string signature)
        => Verify(payload, secret, string.Empty, signature);

    public bool Verify(string payload, string secret, string timestamp, string signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)) return false;
        var expected = Sign(payload, secret, timestamp);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}

public sealed class Sha256IdempotencyRequestHasher : IIdempotencyRequestHasher
{
    public string Hash(string requestBody)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(requestBody ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
