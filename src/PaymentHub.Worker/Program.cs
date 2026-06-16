using Microsoft.Extensions.Hosting;
using PaymentHub.Application.Abstractions.Outbox;
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
            builder.Services.AddHttpClient("application-webhook");
            builder.Services.AddScoped<IApplicationWebhookDispatcher, NoopApplicationWebhookDispatcher>();

            builder.Services.AddHostedService<WebhookProcessorWorker>();
            builder.Services.AddHostedService<OutboxDispatcherWorker>();

            var host = builder.Build();
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

internal sealed class NoopApplicationWebhookDispatcher : IApplicationWebhookDispatcher
{
    public Task DispatchAsync(Domain.Entities.OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        Log.Warning(
            "Outbox event {OutboxId} ({EventType}) for tenant {TenantId} has no dispatcher configured in the Worker process; configure HTTP dispatcher via API process or a dedicated dispatcher service.",
            outboxEvent.Id, outboxEvent.EventType, outboxEvent.TenantId);
        return Task.CompletedTask;
    }
}
