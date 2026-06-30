using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Feature-flag policy implementation backed by the
/// <c>Providers:AbacatePay:AllowWebhookRegistration</c> config flag.
/// Returns <c>true</c> only when the operator explicitly opted in for
/// the requested provider. Any other provider returns <c>false</c>
/// unconditionally because Slice 2-C only supports AbacatePay.
/// </summary>
public sealed class AbacatePayWebhookRegistrationFeaturePolicy
    : IProviderWebhookRegistrationFeaturePolicy
{
    private readonly IOptionsMonitor<AbacatePayOptions> _options;

    public AbacatePayWebhookRegistrationFeaturePolicy(
        IOptionsMonitor<AbacatePayOptions> options)
    {
        _options = options;
    }

    public bool IsRemoteRegistrationEnabled(ProviderCode code)
        => code == ProviderCode.AbacatePay
           && _options.CurrentValue.AllowWebhookRegistration;
}
