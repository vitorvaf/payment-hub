using System.Text.Json;
using PaymentHub.Application.Abstractions.Security;

namespace PaymentHub.Application.Tenants;

/// <summary>
/// Pure helper used by the Slice 2-C webhook handlers to inspect the
/// contents of <c>ProviderAccount.EncryptedCredentials</c> without
/// leaking the secret itself.
///
/// Behaviour:
/// - <see cref="HasWebhookSecret"/> returns <c>true</c> when the
///   unprotected JSON contains a non-empty <c>webhookSecret</c> string
///   field — preferred — OR a non-empty legacy <c>secret</c> string.
/// - <see cref="UnprotectAndReadApiKey"/> returns the apiKey for use
///   inside the configure-handler flow (where we are about to round-trip
///   the credentials through <c>ICredentialProtector</c> anyway).
/// - No side effects, no logging, no exceptions — bad JSON or
///   unprotectable blobs yield defaults.
///
/// Mirrors the logic that already lives in
/// <c>ProcessWebhookEventHandler.ExtractWebhookSecret</c> (Slice 2-B)
/// but lives in the Application layer so the webhook-management
/// handler does not need to know about implementation details.
/// </summary>
internal static class ProviderAccountCredentialsInspector
{
    /// <summary>
    /// Returns <c>true</c> when the blob decrypted from
    /// <paramref name="encryptedCredentials"/> carries a webhook secret —
    /// either via the explicit <c>webhookSecret</c> field (preferred) or
    /// via the legacy <c>secret</c> field (backwards compatibility).
    /// Returns <c>false</c> when the blob cannot be unprotected, when
    /// the JSON cannot be parsed, or when no secret-shaped field is
    /// present. Never throws.
    /// </summary>
    public static bool HasWebhookSecret(
        ICredentialProtector protector,
        string encryptedCredentials)
    {
        if (protector is null) return false;
        if (string.IsNullOrWhiteSpace(encryptedCredentials)) return false;
        if (!TryUnprotect(protector, encryptedCredentials, out var json)) return false;

        return TryFindWebhookSecret(json, out _);
    }

    /// <summary>
    /// Best-effort read of <c>apiKey</c> from the unprotected blob.
    /// Returns <c>null</c> when the blob cannot be unprotected or does
    /// not contain an <c>apiKey</c> string.
    /// </summary>
    public static string? UnprotectAndReadApiKey(
        ICredentialProtector protector,
        string encryptedCredentials)
    {
        if (protector is null) return null;
        if (string.IsNullOrWhiteSpace(encryptedCredentials)) return null;
        if (!TryUnprotect(protector, encryptedCredentials, out var json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("apiKey", out var apiKey)) return null;
            if (apiKey.ValueKind != JsonValueKind.String) return null;
            var value = apiKey.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a fresh JSON payload from the existing unprotected
    /// credentials, preserving <c>apiKey</c> and adopting
    /// <paramref name="newWebhookSecret"/> when provided.
    /// Returns <c>null</c> when the existing blob cannot be unprotected
    /// or when no <c>apiKey</c> is present.
    /// </summary>
    /// <param name="newWebhookSecret">
    /// Pass <c>null</c> to keep the existing webhook-secret value; pass
    /// an explicit string to overwrite (including clearing by passing
    /// <c>string.Empty</c> would still write an empty string here, which
    /// the caller decides).
    /// </param>
    public static string? BuildMergedCredentialsJson(
        ICredentialProtector protector,
        string encryptedCredentials,
        string? newWebhookSecret,
        bool overwriteWebhookSecret)
    {
        if (protector is null) return null;
        if (string.IsNullOrWhiteSpace(encryptedCredentials)) return null;
        if (!TryUnprotect(protector, encryptedCredentials, out var existingJson)) return null;

        var apiKey = UnprotectAndReadApiKey(protector, encryptedCredentials);
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        string? currentWebhookSecret = null;
        string? currentLegacySecret = null;
        try
        {
            using var doc = JsonDocument.Parse(existingJson);
            if (doc.RootElement.TryGetProperty("webhookSecret", out var ws) && ws.ValueKind == JsonValueKind.String)
                currentWebhookSecret = ws.GetString();
            if (doc.RootElement.TryGetProperty("secret", out var ls) && ls.ValueKind == JsonValueKind.String)
                currentLegacySecret = ls.GetString();
        }
        catch (JsonException)
        {
            // Treat malformed JSON as "no current secret information".
        }

        var resolvedWebhookSecret = overwriteWebhookSecret
            ? newWebhookSecret
            : (currentWebhookSecret ?? currentLegacySecret);

        if (string.IsNullOrWhiteSpace(resolvedWebhookSecret))
        {
            // Pure apiKey-only payload — keeps backwards compatibility
            // with accounts registered before webhook configuration existed.
            return JsonSerializer.Serialize(new { apiKey });
        }

        return JsonSerializer.Serialize(new
        {
            apiKey,
            webhookSecret = resolvedWebhookSecret
        });
    }

    private static bool TryUnprotect(
        ICredentialProtector protector,
        string encryptedCredentials,
        out string plainJson)
    {
        plainJson = null!;
        try
        {
            var plain = protector.Unprotect(encryptedCredentials);
            if (string.IsNullOrWhiteSpace(plain)) return false;
            plainJson = plain;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindWebhookSecret(string plainJson, out string? secret)
    {
        secret = null;
        try
        {
            using var doc = JsonDocument.Parse(plainJson);
            // Prefer explicit webhookSecret, fall back to legacy secret.
            if (doc.RootElement.TryGetProperty("webhookSecret", out var ws)
                && ws.ValueKind == JsonValueKind.String)
            {
                var value = ws.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    secret = value;
                    return true;
                }
            }
            if (doc.RootElement.TryGetProperty("secret", out var ls)
                && ls.ValueKind == JsonValueKind.String)
            {
                var value = ls.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    secret = value;
                    return true;
                }
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
