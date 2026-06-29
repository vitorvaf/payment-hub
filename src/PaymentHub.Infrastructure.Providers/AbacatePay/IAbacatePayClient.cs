using PaymentHub.Infrastructure.Providers.AbacatePay.Models;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Typed HTTP client for the AbacatePay sandbox/devMode REST API. The adapter
/// layer owns the <c>apiKey</c> parameter (extracted from <c>EncryptedCredentials</c>)
/// so this contract never carries tenant credentials across method boundaries.
/// </summary>
public interface IAbacatePayClient
{
    /// <summary>
    /// Calls <c>POST /transparents/create</c> with <c>Authorization: Bearer apiKey</c>.
    /// Throws <see cref="AbacatePayClientException"/> on any categorized failure.
    /// </summary>
    Task<AbacatePayCreateTransparentPixResponse> CreateTransparentPixAsync(
        AbacatePayCreateTransparentPixRequest request,
        string apiKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Calls <c>GET /transparents/check?id=...</c> with <c>Authorization: Bearer apiKey</c>.
    /// Used by the concrete adapter for internal status sync (not exposed on
    /// <c>IPaymentProviderAdapter</c> in this slice).
    /// </summary>
    Task<AbacatePayCheckTransparentPixResponse> CheckTransparentPixAsync(
        string providerPaymentId,
        string apiKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Calls <c>POST /transparents/simulate-payment</c> in sandbox devMode only.
    /// Throws <see cref="AbacatePayClientException"/> with
    /// <see cref="AbacatePayErrorCategory.SimulationDisabled"/> when
    /// <see cref="AbacatePayOptions.AllowDevModeSimulation"/> is <c>false</c>.
    /// </summary>
    Task<AbacatePaySimulatePaymentResponse> SimulateTransparentPixPaymentAsync(
        string providerPaymentId,
        string apiKey,
        CancellationToken cancellationToken);
}