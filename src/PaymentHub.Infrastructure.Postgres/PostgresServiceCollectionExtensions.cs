using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Infrastructure.Postgres.Outbox;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.Infrastructure.Postgres.Repositories;
using PaymentHub.Infrastructure.Postgres.Security;
using PaymentHub.Infrastructure.Postgres.Webhooks;

namespace PaymentHub.Infrastructure.Postgres;

public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Named HttpClient consumed by <see cref="HttpApplicationWebhookDispatcher"/>. The name is
    /// referenced by the dispatcher via <c>IHttpClientFactory.CreateClient(name)</c>. Co-located
    /// with the dispatcher registration so a host that calls <c>AddPaymentHubPostgres</c> ends
    /// up with a fully wired outbound webhook pipeline (Slice 7-A, ADR-0010).
    /// </summary>
    public const string ApplicationWebhookHttpClientName = "application-webhook";

    public static IServiceCollection AddPaymentHubPostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaymentHubOptions>(configuration.GetSection(PaymentHubOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.AddDbContext<PaymentHubDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(PaymentHubDbContext).Assembly.GetName().Name)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IApplicationClientRepository, ApplicationClientRepository>();
        services.AddScoped<IProviderAccountRepository, ProviderAccountRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
        services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<Application.Abstractions.Outbox.IOutboxRepository, OutboxRepository>();
        services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        services.AddScoped<IOutboxEventStore, EfOutboxEventStore>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IApiKeyHasher, HmacApiKeyHasher>();
        services.AddSingleton<ICredentialProtector, AesCredentialProtector>();
        services.AddSingleton<IWebhookSecretProtector, AesWebhookSecretProtector>();
        services.AddSingleton<IWebhookSigner, HmacWebhookSigner>();
        services.AddSingleton<IIdempotencyRequestHasher, Sha256IdempotencyRequestHasher>();

        // Slice 2-C.1: public read of apiKey from ProviderAccount.EncryptedCredentials
        // for cross-layer use (e.g. the AbacatePayWebhookManagementClient needs the
        // apiKey in plaintext to send a Bearer header; it must NOT receive the
        // protected blob's content from the handler).
        services.AddSingleton<IProviderAccountCredentialsReader, ProviderAccountCredentialsReader>();

        // Outbound webhook dispatcher (Slice 7-A, resolves P1-4). Co-located with the HttpClient
        // factory registration so API and Worker hosts end up with the same named client and
        // the same dispatcher implementation.
        services.AddHttpClient(ApplicationWebhookHttpClientName);
        services.AddScoped<IApplicationWebhookDispatcher, HttpApplicationWebhookDispatcher>();

        return services;
    }
}

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
