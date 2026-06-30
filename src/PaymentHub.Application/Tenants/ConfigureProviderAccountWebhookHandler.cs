using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Tenants;

/// <summary>
/// Tagged outcome of <see cref="IConfigureProviderAccountWebhookHandler"/>.
/// Lives at the Application boundary so the controller can map cleanly to
/// <c>200 OK</c>/<c>404 Not Found</c>/<c>409 Conflict</c>/<c>422 Unprocessable Entity</c>
/// without leaking implementation details into the HTTP layer.
///
/// SECURITY: <c>Success</c> never carries <c>apiKey</c>, raw
/// <c>webhookSecret</c>, the protected blob or anything else sensitive.
/// </summary>
public abstract record ConfigureWebhookOutcome
{
    private ConfigureWebhookOutcome() { }

    public sealed record Success(ProviderAccountWebhookResponseDto Response) : ConfigureWebhookOutcome;

    /// <summary>
    /// The provider account id was not found in the authenticated
    /// tenant/application scope. Maps to <c>404 Not Found</c>.
    /// </summary>
    public sealed record NotFound : ConfigureWebhookOutcome;

    /// <summary>
    /// The provider account exists in scope but is marked inactive
    /// (<c>Active = false</c>). Maps to <c>409 Conflict</c>. No state
    /// is changed — credential updates are intentionally rejected when
    /// the account is inactive.
    /// </summary>
    public sealed record Inactive : ConfigureWebhookOutcome;

    /// <summary>
    /// The provider code of the account is not AbacatePay. Slice 2-C
    /// only supports managing AbacatePay webhooks; the other providers
    /// do not yet expose management endpoints. Maps to <c>409 Conflict</c>.
    /// </summary>
    public sealed record UnsupportedProvider : ConfigureWebhookOutcome;
}

public interface IConfigureProviderAccountWebhookHandler
{
    Task<ConfigureWebhookOutcome> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        Guid providerAccountId,
        ConfigureAbacatePayWebhookRequestDto request,
        CancellationToken cancellationToken);
}

public sealed class ConfigureProviderAccountWebhookHandler
    : IConfigureProviderAccountWebhookHandler
{
    private readonly IProviderAccountRepository _accounts;
    private readonly ICredentialProtector _protector;
    private readonly IUnitOfWork _uow;
    private readonly IProviderWebhookManagementClient _webhookClient;
    private readonly IProviderWebhookRegistrationFeaturePolicy _registrationFeaturePolicy;
    private readonly ILogger<ConfigureProviderAccountWebhookHandler> _logger;

    public ConfigureProviderAccountWebhookHandler(
        IProviderAccountRepository accounts,
        ICredentialProtector protector,
        IUnitOfWork uow,
        IProviderWebhookManagementClient webhookClient,
        IProviderWebhookRegistrationFeaturePolicy registrationFeaturePolicy,
        ILogger<ConfigureProviderAccountWebhookHandler> logger)
    {
        _accounts = accounts;
        _protector = protector;
        _uow = uow;
        _webhookClient = webhookClient;
        _registrationFeaturePolicy = registrationFeaturePolicy;
        _logger = logger;
    }

    public async Task<ConfigureWebhookOutcome> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        Guid providerAccountId,
        ConfigureAbacatePayWebhookRequestDto request,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("Authenticated tenant id is required.");
        if (applicationId == Guid.Empty)
            throw new InvalidOperationException("Authenticated application id is required.");
        if (providerAccountId == Guid.Empty)
            throw new InvalidOperationException("ProviderAccount id is required.");

        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var account = await _accounts.GetByIdForTenantAndApplicationAsync(
            tenantId, applicationId, providerAccountId, cancellationToken);
        if (account is null) return new ConfigureWebhookOutcome.NotFound();

        if (!account.Active) return new ConfigureWebhookOutcome.Inactive();

        if (account.ProviderCode != ProviderCode.AbacatePay)
            return new ConfigureWebhookOutcome.UnsupportedProvider();

        // ---- Step 1: rebuild encrypted credentials preserving apiKey ----
        // The inspector knows how to round-trip the existing JSON blob
        // through `ICredentialProtector`. We refuse to overwrite when
        // we cannot recover the apiKey — that's an unrecoverable state
        // and silently producing a credentials-blob without an apiKey
        // would break downstream checkout / create flows.
        var overwriteSecret = request.WebhookSecret is not null;
        var mergedJson = ProviderAccountCredentialsInspector.BuildMergedCredentialsJson(
            _protector,
            account.EncryptedCredentials,
            request.WebhookSecret,
            overwriteSecret);
        if (mergedJson is null)
        {
            _logger.LogWarning(
                "ProviderAccount {ProviderAccountId} carries credentials that cannot be unprotected/merged during webhook configuration.",
                account.Id);
            throw new InvalidOperationException(
                "ProviderAccount credentials are not in a recognised format and cannot be updated safely.");
        }

        var protectedCredentials = _protector.Protect(mergedJson);
        account.UpdateCredentials(protectedCredentials);

        // ---- Step 2: persist the non-sensitive webhook configuration ----
        var eventsJson = request.Events is null || request.Events.Count == 0
            ? null
            : JsonSerializer.Serialize(request.Events);

        // The local-only status is recorded deterministically: remote
        // registration either did not happen or was deferred. If a real
        // remote attempt happens below, we update the entity again.
        var deferredStatus = request.RegisterRemotely
            ? ProviderWebhookRemoteStatus.RemoteRegistrationDeferred
            : ProviderWebhookRemoteStatus.NotRegistered;
        account.ConfigureWebhook(request.CallbackUrl, eventsJson, deferredStatus);

        await _accounts.UpdateAsync(account, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        // ---- Step 3: optionally call the upstream provider API ----
        // Three gates must line up for a real HTTP call to happen:
        //   * The caller explicitly opted in via `registerRemotely=true`.
        //   * A new `webhookSecret` was supplied (we don't try to
        //     re-register with the same secret).
        //   * The feature policy allows it (default is opt-out).
        // If any gate fails we keep the local-only status that was
        // already saved (either `NotRegistered` or
        // `RemoteRegistrationDeferred`).
        if (request.RegisterRemotely
            && request.WebhookSecret is not null
            && _registrationFeaturePolicy.IsRemoteRegistrationEnabled(account.ProviderCode))
        {
            var outcome = await _webhookClient.RegisterWebhookAsync(
                account.ProviderCode,
                protectedCredentials,
                request.WebhookSecret,
                request.CallbackUrl ?? account.WebhookCallbackUrl!,
                request.Events ?? Array.Empty<string>(),
                cancellationToken);

            var mappedStatus = outcome switch
            {
                ProviderWebhookRegistrationOutcome.Registered => ProviderWebhookRemoteStatus.Registered,
                ProviderWebhookRegistrationOutcome.RegistrationFailed => ProviderWebhookRemoteStatus.RegistrationFailed,
                _ => ProviderWebhookRemoteStatus.RegistrationFailed
            };
            // Persist the final remote status but keep callbackUrl and
            // events intact.
            account.ConfigureWebhook(account.WebhookCallbackUrl, account.WebhookEvents, mappedStatus);
            await _accounts.UpdateAsync(account, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return new ConfigureWebhookOutcome.Success(BuildResponse(account));
    }

    private ProviderAccountWebhookResponseDto BuildResponse(ProviderAccount account)
    {
        var hasSecret = ProviderAccountCredentialsInspector.HasWebhookSecret(
            _protector,
            account.EncryptedCredentials);

        return new ProviderAccountWebhookResponseDto(
            ProviderAccountId: account.Id,
            ProviderCode: account.ProviderCode,
            Environment: account.Environment,
            CallbackUrl: account.WebhookCallbackUrl,
            Events: ParseEventsOrEmpty(account.WebhookEvents),
            HasWebhookSecret: hasSecret,
            RemoteRegistrationStatus: account.WebhookRemoteStatus?.ToString(),
            ConfiguredAt: account.WebhookConfiguredAt,
            UpdatedAt: account.UpdatedAt);
    }

    private static IReadOnlyList<string> ParseEventsOrEmpty(string? eventsJson)
    {
        if (string.IsNullOrWhiteSpace(eventsJson)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(eventsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var output = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var s = element.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) output.Add(s);
                }
            }
            return output.Count == 0 ? Array.Empty<string>() : output;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
