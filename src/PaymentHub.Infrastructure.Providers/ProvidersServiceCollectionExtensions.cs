using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Infrastructure.Providers.AbacatePay;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;
using PaymentHub.Infrastructure.Providers.Fake;
using PaymentHub.Infrastructure.Providers.MercadoPago;
using PaymentHub.Infrastructure.Providers.Routing;
using PaymentHub.Infrastructure.Providers.Stripe;

namespace PaymentHub.Infrastructure.Providers;

public static class ProvidersServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentHubProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options binding — section "Providers:AbacatePay". Safe defaults live
        // in AbacatePayOptions itself; appsettings only override what they want.
        services.Configure<AbacatePayOptions>(configuration.GetSection(AbacatePayOptions.SectionName));

        // Named HttpClient for AbacatePay. Timeout comes from AbacatePayOptions,
        // enabling test overrides without touching HttpClient defaults. The
        // client lifetime is managed by IHttpClientFactory; AbacatePayClient
        // pulls a fresh client per call so DNS / socket rotation is automatic.
        services.AddHttpClient(AbacatePayClient.HttpClientName, (sp, http) =>
        {
            var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AbacatePayOptions>>();
            var opts = optionsMonitor.CurrentValue;
            http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        // AbacatePayClient and the concrete adapter only depend on singleton
        // services (IHttpClientFactory, IOptionsMonitor, ILogger) so they can
        // both stay Singleton — no captive-dependency risk.
        services.AddSingleton<IAbacatePayClient, AbacatePayClient>();
        services.AddSingleton<IPaymentProviderAdapter, AbacatePayProviderAdapter>();

        // Slice 2-B: HMAC webhook signature verifier + event normalizer.
        // Both are pure: zero side-effects, deterministic, single-threaded.
        services.AddSingleton<IAbacatePayWebhookSignatureVerifier, HmacAbacatePayWebhookSignatureVerifier>();
        services.AddSingleton<IAbacatePayWebhookNormalizer, AbacatePayWebhookNormalizer>();

        // Slice 2-C.1: named HttpClient dedicated to webhook management calls.
        // Kept distinct from `abacatepay` (which serves transparent create/check/simulate)
        // so the webhook-management lifecycle can be tuned independently in the
        // future (different timeout, retry policy, rate-limit reservation).
        services.AddHttpClient(AbacatePayWebhookManagementClient.HttpClientName, (sp, http) =>
        {
            var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AbacatePayOptions>>();
            var opts = optionsMonitor.CurrentValue;
            http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        // Slice 2-C.1: real HTTP client replacing the Slice 2-C no-op. The
        // feature flag (`Providers:AbacatePay:AllowWebhookRegistration`)
        // remains opt-in; default false in `appsettings.json`. The handler
        // already short-circuits when the flag is off, so this client only
        // emits traffic when explicitly authorised by both the handler and
        // the feature policy.
        services.AddSingleton<IProviderWebhookManagementClient, AbacatePayWebhookManagementClient>();
        services.AddSingleton<IProviderWebhookRegistrationFeaturePolicy, AbacatePayWebhookRegistrationFeaturePolicy>();

        // Other providers remain skeleton for now.
        services.AddSingleton<IPaymentProviderAdapter, FakePaymentProviderAdapter>();
        services.AddSingleton<IPaymentProviderAdapter, StripeProviderAdapter>();
        services.AddSingleton<IPaymentProviderAdapter, MercadoPagoProviderAdapter>();

        services.AddSingleton<IPaymentProviderRouter>(sp =>
        {
            var adapters = sp.GetServices<IPaymentProviderAdapter>();
            var defaultProvider = configuration["PaymentHub:DefaultProvider"] ?? "Fake";
            var logger = sp.GetRequiredService<ILogger<PaymentProviderRouter>>();
            logger.LogInformation("Payment provider router configured with default '{DefaultProvider}'", defaultProvider);
            return new PaymentProviderRouter(adapters, defaultProvider);
        });

        return services;
    }
}