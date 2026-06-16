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

public interface IWebhookSigner
{
    string Sign(string payload, string secret);
    bool Verify(string payload, string secret, string signature);
}

public interface IIdempotencyRequestHasher
{
    string Hash(string requestBody);
}
