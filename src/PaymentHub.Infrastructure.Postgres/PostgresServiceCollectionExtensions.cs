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

namespace PaymentHub.Infrastructure.Postgres;

public static class PostgresServiceCollectionExtensions
{
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

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IApiKeyHasher, HmacApiKeyHasher>();
        services.AddSingleton<ICredentialProtector, AesCredentialProtector>();
        services.AddSingleton<IWebhookSigner, HmacWebhookSigner>();
        services.AddSingleton<IIdempotencyRequestHasher, Sha256IdempotencyRequestHasher>();

        return services;
    }
}

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
