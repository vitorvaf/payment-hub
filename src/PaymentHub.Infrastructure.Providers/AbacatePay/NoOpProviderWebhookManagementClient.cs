using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// No-op <see cref="IProviderWebhookManagementClient"/> for Slice 2-C.
///
/// AbacatePay registration is intentionally deferred to a follow-up
/// slice gated by <c>Providers:AbacatePay:AllowWebhookRegistration</c>.
/// Until that slice lands, this client behaves like a server
/// returning <see cref="ProviderWebhookRegistrationOutcome.Registered"/>
/// so the configure-handler can exercise its success path without
/// triggering an HTTP request.
///
/// SECURITY: never logs <c>protectedCredentials</c>, the plaintext
/// <c>webhookSecret</c>, the raw callback body or any signature. Logs
/// only bookkeeping (callback length, events count).
/// </summary>
public sealed class NoOpProviderWebhookManagementClient
    : IProviderWebhookManagementClient
{
    private readonly ILogger<NoOpProviderWebhookManagementClient> _logger;

    public NoOpProviderWebhookManagementClient(
        ILogger<NoOpProviderWebhookManagementClient> logger)
    {
        _logger = logger;
    }

    public Task<ProviderWebhookRegistrationOutcome> RegisterWebhookAsync(
        Domain.Enums.ProviderCode providerCode,
        string protectedCredentials,
        string webhookSecret,
        string callbackUrl,
        IReadOnlyList<string> events,
        CancellationToken cancellationToken)
    {
        // SECURITY: never log protectedCredentials, webhookSecret, the
        // raw callbackUrl body, or the resolved payload. We log only
        // the bookkeeping we need to confirm the no-op path ran.
        _logger.LogInformation(
            "NoOpProviderWebhookManagementClient: remote registration skipped (provider={ProviderCode}, callback length={CallbackLength}, events={EventCount}).",
            providerCode,
            callbackUrl?.Length ?? 0,
            events?.Count ?? 0);

        return Task.FromResult(ProviderWebhookRegistrationOutcome.Registered);
    }
}
