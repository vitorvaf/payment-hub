using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.Infrastructure.Postgres.Outbox;
using PaymentHub.Infrastructure.Postgres.Repositories;
using PaymentHub.Infrastructure.Postgres.Security;

namespace PaymentHub.IntegrationTests.Infrastructure;

/// <summary>
/// Lightweight factory used by every integration test in this assembly.
/// It deliberately wires ONLY the persistence + crypto + outbox surface we
/// want to exercise, instead of calling <c>AddPaymentHubPostgres</c> from
/// the host extension method (which would pull in the HTTP webhook
/// dispatcher and the API key hasher). This keeps the fixture small,
/// fast, and free of unused dependencies.
///
/// All <see cref="PaymentHubOptions"/> keys are populated with deterministic
/// 32-byte values so the <see cref="AesWebhookSecretProtector"/> and
/// <see cref="AesCredentialProtector"/> accept them without throwing.
/// </summary>
public sealed class IntegrationTestFactory
{
    private const string WebhookSecretKey = "integration-test-webhook-secret-key-32+chars!";
    private const string CredentialEncryptionKey = "integration-test-credential-encryption-key-32+";
    private const string ApiKeyHashSecret = "integration-test-api-key-hash-secret-32+chars!";

    private readonly PostgresFixture _fixture;
    private readonly IServiceProvider _services;

    public IntegrationTestFactory(PostgresFixture fixture)
    {
        _fixture = fixture;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = Options.Create(new PaymentHubOptions
        {
            ApiKeyHashSecret = ApiKeyHashSecret,
            CredentialEncryptionKey = CredentialEncryptionKey,
            WebhookSecretEncryptionKey = WebhookSecretKey,
        });
        services.AddSingleton(options);

        services.AddSingleton<IClock, IntegrationTestClock>();
        services.AddSingleton<IApiKeyHasher, HmacApiKeyHasher>();
        services.AddSingleton<ICredentialProtector, AesCredentialProtector>();
        services.AddSingleton<IWebhookSecretProtector, AesWebhookSecretProtector>();
        services.AddSingleton<IWebhookSigner, HmacWebhookSigner>();
        services.AddSingleton<IIdempotencyRequestHasher, Sha256IdempotencyRequestHasher>();

        services.AddScoped<PaymentHubDbContext>(_ => _fixture.BuildContext());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IApplicationClientRepository, ApplicationClientRepository>();
        services.AddScoped<IProviderAccountRepository, ProviderAccountRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
        services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        services.AddScoped<IOutboxEventStore, EfOutboxEventStore>();

        _services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Opens a fresh DI scope so each test gets its own <see cref="PaymentHubDbContext"/>
    /// and repository instances (which is required because they are scoped).
    /// </summary>
    public IServiceScope CreateScope() => _services.CreateScope();

    /// <summary>
    /// Returns a new <see cref="PaymentHubDbContext"/> backed by the test container.
    /// Prefer <see cref="CreateScope"/> for tests that exercise repositories.
    /// </summary>
    public PaymentHubDbContext CreateDbContext() => _fixture.BuildContext();

    public IWebhookSecretProtector CreateWebhookSecretProtector()
        => _services.GetRequiredService<IWebhookSecretProtector>();

    /// <summary>
    /// Resets the test database to a known empty state by truncating every
    /// application table in topological order (children first to keep the
    /// FK constraints happy). Safe to call between tests in the same
    /// collection because the schema and seed of the container are preserved.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        const string truncateSql = @"
TRUNCATE TABLE
    payment_attempts,
    payments,
    webhook_events,
    outbox_events,
    idempotency_keys,
    api_keys,
    provider_accounts,
    audit_logs,
    application_clients,
    tenants
RESTART IDENTITY CASCADE;";

        await using var context = _fixture.BuildContext();
        await context.Database.ExecuteSqlRawAsync(truncateSql);
    }

    // ------------------------------------------------------------------
    // Convenience seed helpers (idempotent for fixed ids per test method).
    // ------------------------------------------------------------------

    public async Task<Tenant> SeedTenantAsync(
        Guid? id = null,
        string name = "Acme IT",
        string slug = "acme-it",
        bool activate = true,
        CancellationToken cancellationToken = default)
    {
        var tenant = new Tenant(id ?? Guid.NewGuid(), name, slug);
        if (!activate)
        {
            tenant.Suspend();
        }

        using var scope = CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(tenant, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    public async Task<ApplicationClient> SeedApplicationClientAsync(
        Guid tenantId,
        Guid? id = null,
        string name = "App-IT",
        string? webhookUrl = null,
        string? protectedWebhookSecret = null,
        bool activate = true,
        CancellationToken cancellationToken = default)
    {
        var client = new ApplicationClient(
            id ?? Guid.NewGuid(),
            tenantId,
            name,
            webhookUrl,
            protectedWebhookSecret);
        if (!activate)
        {
            client.Suspend();
        }

        using var scope = CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IApplicationClientRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(client, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task<ProviderAccount> SeedProviderAccountAsync(
        Guid tenantId,
        Guid applicationId,
        ProviderCode providerCode = ProviderCode.Fake,
        ProviderEnvironment environment = ProviderEnvironment.Sandbox,
        string name = "Acme Fake Sandbox",
        string encryptedCredentials = "encrypted-fake-credentials-placeholder",
        bool isDefault = true,
        CancellationToken cancellationToken = default)
    {
        var account = new ProviderAccount(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            providerCode,
            environment,
            name,
            encryptedCredentials,
            isDefault);

        using var scope = CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(account, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<OutboxEvent> EnqueueOutboxAsync(
        Guid tenantId,
        Guid applicationId,
        string eventType = "payment.status.changed",
        object? payload = null,
        Guid? id = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var outboxId = id ?? Guid.NewGuid();
        await publisher.EnqueueAsync(
            outboxId,
            tenantId,
            applicationId,
            eventType,
            payload ?? new { test = "1it" },
            cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        // Reload so callers receive a tracked-less, fully-hydrated entity.
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var loaded = await repo.GetByIdAsync(outboxId, cancellationToken);
        return loaded ?? throw new InvalidOperationException(
            $"Outbox event {outboxId} was not persisted.");
    }

    /// <summary>
    /// Minimal <see cref="IClock"/> used by tests. Currently the repository
    /// helpers do not consume it (they go through EF Core + Postgres current
    /// timestamp defaults for <c>CreatedAt</c>), but having the abstraction
    /// wired keeps future slices aligned with <c>PostgresServiceCollectionExtensions</c>.
    /// </summary>
    private sealed class IntegrationTestClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
