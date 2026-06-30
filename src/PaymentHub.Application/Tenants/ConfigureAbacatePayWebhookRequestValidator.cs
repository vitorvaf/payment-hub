using FluentValidation;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Application.Tenants.Validation;

namespace PaymentHub.Application.Tenants;

/// <summary>
/// Validates the body of
/// <c>PUT /api/v1/provider-accounts/{providerAccountId}/webhook</c> (Slice 2-C).
///
/// Rules:
/// - <c>CallbackUrl</c>: optional. When provided, MUST be a well-formed
///   absolute URL passing <c>WebhookUrlValidator.IsAllowed</c> (HTTPS in
///   production; HTTP only allowed for loopback hosts in Development).
/// - <c>Events</c>: optional. When provided, every entry MUST be a
///   non-empty event name from the closed whitelist of supported
///   AbacatePay transparent webhook events.
/// - <c>WebhookSecret</c>: optional. When provided, MUST be 16-500 chars.
///   The body never carries a secret in protected form — the handler is
///   the only layer that knows how to round-trip
///   <c>ICredentialProtector.Protect/Unprotect</c>.
/// - <c>RegisterRemotely</c>: optional bool. When true, the handler may
///   attempt a real AbacatePay registration call (gated by feature flag).
///   Default is <c>false</c> — only local configuration is persisted.
/// </summary>
public sealed class ConfigureAbacatePayWebhookRequestValidator
    : AbstractValidator<ConfigureAbacatePayWebhookRequestDto>
{
    /// <summary>
    /// Whitelist of AbacatePay transparent webhook event types that the
    /// configuration endpoint allows. Kept as a literal inside the
    /// Application layer so the validator never has to depend on
    /// Infrastructure. Any change here must be matched in the AbacatePay
    /// normalizer (Infrastructure.Providers.AbacatePay.Webhooks).
    /// </summary>
    internal static readonly IReadOnlySet<string> AllowedAbacatePayWebhookEvents =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "transparent.completed",
            "transparent.refunded",
            "transparent.disputed",
            "transparent.lost"
        };

    public ConfigureAbacatePayWebhookRequestValidator(IRuntimeEnvironment environment)
    {
        // CallbackUrl — only validate when supplied. The helper covers
        // HTTPS-only, SSRF (loopback/RFC1918/link-local), and
        // Development loopback HTTP exception.
        RuleFor(x => x.CallbackUrl)
            .MaximumLength(2000)
            .Must((dto, value) =>
            {
                if (string.IsNullOrWhiteSpace(value)) return true;
                return WebhookUrlValidator.IsAllowed(value, environment.IsDevelopment, out _);
            })
            .WithMessage("CallbackUrl must be a public HTTPS URL (HTTP only allowed for loopback hosts in Development).")
            .When(x => x.CallbackUrl is not null);

        // Events — every entry MUST be in the whitelist.
        RuleFor(x => x.Events!)
            .Must(events => events!.All(e => !string.IsNullOrWhiteSpace(e)))
            .WithMessage("Events entries cannot be empty or whitespace.")
            .Must(events => events!.All(AllowedAbacatePayWebhookEvents.Contains))
            .WithMessage("Events contains entries outside the AbacatePay transparent.* whitelist.")
            .When(x => x.Events is not null && x.Events.Count > 0);

        // WebhookSecret — length is the only structural check. Never log,
        // never echo. The handler protects before persisting.
        RuleFor(x => x.WebhookSecret)
            .MinimumLength(16)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.WebhookSecret));
    }
}
