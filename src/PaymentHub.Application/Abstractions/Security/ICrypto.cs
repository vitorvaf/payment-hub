namespace PaymentHub.Application.Abstractions.Security;

public interface IApiKeyHasher
{
    string Hash(string apiKey);
    bool Verify(string apiKey, string hash);
}

public interface ICredentialProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface IWebhookSecretProtector
{
    string Protect(string plainTextSecret);
    string Unprotect(string protectedSecret);
}

public interface IWebhookSigner
{
    string Sign(string payload, string secret);
    string Sign(string payload, string secret, string timestamp);
    bool Verify(string payload, string secret, string signature);
    bool Verify(string payload, string secret, string timestamp, string signature);
}

public interface IIdempotencyRequestHasher
{
    string Hash(string requestBody);
}
