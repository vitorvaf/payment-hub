using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Checkouts;
using PaymentHub.Application.Payments;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Webhooks;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Providers;
using Serilog;
using Serilog.Formatting.Compact;

namespace PaymentHub.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateBootstrapLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddSerilog((sp, lc) => lc
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(new CompactJsonFormatter()));

            builder.Services.AddPaymentHubPostgres(builder.Configuration);
            builder.Services.AddPaymentHubProviders(builder.Configuration);

            builder.Services.AddScoped<IRegisterTenantHandler, RegisterTenantHandler>();
            builder.Services.AddScoped<IRegisterApplicationClientHandler, RegisterApplicationClientHandler>();
            builder.Services.AddScoped<IRegisterProviderAccountHandler, RegisterProviderAccountHandler>();
            builder.Services.AddScoped<ICreateCheckoutHandler, CreateCheckoutHandler>();
            builder.Services.AddScoped<IGetPaymentByIdHandler, GetPaymentByIdHandler>();
            builder.Services.AddScoped<IListPaymentsHandler, ListPaymentsHandler>();
            builder.Services.AddScoped<IReceiveProviderWebhookHandler, ReceiveProviderWebhookHandler>();
            builder.Services.AddScoped<IProcessWebhookEventHandler, ProcessWebhookEventHandler>();

            builder.Services.AddHostedService<WebhookProcessorWorker>();
            builder.Services.AddHostedService<OutboxDispatcherWorker>();

            var host = builder.Build();

            // Fail-fast (Slice 7-A, security-reviewer B2): resolve IWebhookSecretProtector before
            // host.Run() so a misconfigured PaymentHub:WebhookSecretEncryptionKey surfaces as a
            // startup error in the Worker log instead of being deferred to the first outbox
            // dispatch. The dispatcher is registered by AddPaymentHubPostgres.
            using (var scope = host.Services.CreateScope())
            {
                _ = scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>();
            }

            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Payment Hub Worker terminated unexpectedly.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}