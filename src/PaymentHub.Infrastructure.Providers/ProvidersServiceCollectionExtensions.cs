using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Infrastructure.Providers.AbacatePay;
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
        services.AddSingleton<IPaymentProviderAdapter, FakePaymentProviderAdapter>();
        services.AddSingleton<IPaymentProviderAdapter, AbacatePayProviderAdapter>();
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
