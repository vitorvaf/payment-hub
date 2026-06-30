using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Domain.Enums;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// Test double for <see cref="IProviderWebhookManagementClient"/>. Captures
/// the most recent call so tests can assert what was sent. Behavior is
/// configurable: by default returns <see cref="ProviderWebhookRegistrationOutcome.Registered"/>
/// so happy-path tests do not need to set anything up.
/// </summary>
public sealed class FakeProviderWebhookManagementClient
    : IProviderWebhookManagementClient
{
    public int CallCount { get; private set; }
    public ProviderCode? LastProviderCode { get; private set; }
    public string? LastProtectedCredentials { get; private set; }
    public string? LastWebhookSecret { get; private set; }
    public string? LastCallbackUrl { get; private set; }
    public IReadOnlyList<string>? LastEvents { get; private set; }

    /// <summary>
    /// Outcome to return on the next call. Default is
    /// <see cref="ProviderWebhookRegistrationOutcome.Registered"/>;
    /// tests can flip this to <c>RegistrationFailed</c> to exercise the
    /// negative path.
    /// </summary>
    public ProviderWebhookRegistrationOutcome NextOutcome { get; set; }
        = ProviderWebhookRegistrationOutcome.Registered;

    public Task<ProviderWebhookRegistrationOutcome> RegisterWebhookAsync(
        ProviderCode providerCode,
        string protectedCredentials,
        string webhookSecret,
        string callbackUrl,
        IReadOnlyList<string> events,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastProviderCode = providerCode;
        LastProtectedCredentials = protectedCredentials;
        LastWebhookSecret = webhookSecret;
        LastCallbackUrl = callbackUrl;
        LastEvents = events;
        return Task.FromResult(NextOutcome);
    }
}

/// <summary>
/// Test double for <see cref="IProviderWebhookRegistrationFeaturePolicy"/>.
/// Defaults to "feature off" so tests that need remote registration must
/// opt in by setting <see cref="AllowRemoteRegistration"/> to <c>true</c>.
/// </summary>
public sealed class FakeProviderWebhookRegistrationFeaturePolicy
    : IProviderWebhookRegistrationFeaturePolicy
{
    public bool AllowRemoteRegistration { get; set; }
    public ProviderCode? LastCode { get; private set; }
    public int CallCount { get; private set; }

    public bool IsRemoteRegistrationEnabled(ProviderCode code)
    {
        CallCount++;
        LastCode = code;
        return AllowRemoteRegistration;
    }
}
