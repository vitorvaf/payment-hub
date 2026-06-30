using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.Infrastructure.Postgres.Security;
using PaymentHub.IntegrationTests.Support;

namespace PaymentHub.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real <c>PaymentHub.Api</c> Program in-memory via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>. Overrides configuration
/// to point at the Testcontainers Postgres from <see cref="PostgresFixture"/>,
/// disables the development bootstrap seeder, and substitutes the
/// <c>abacatepay</c> and <c>application-webhook</c> named HttpClients with
/// deterministic fakes so no outbound call ever leaves the test process.
/// </summary>
/// <remarks>
/// <para>
/// The factory is the source of truth for E2E configuration. Every test
/// class shares a single <see cref="PostgresFixture"/> (one container per
/// run) and may create one factory per class so the captured HTTP state
/// stays predictable.
/// </para>
/// <para>
/// Tests reset the database between runs via <see cref="ResetDatabaseAsync"/>.
/// The factory itself boots once per instance.
/// </para>
/// </remarks>
public sealed class PaymentHubApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Fake keys + salts used by every E2E test. Real secrets MUST NOT be
    /// committed — these values mirror the 32+ byte deterministic blobs
    /// already used by the Slice 1-IT integration fixture.
    /// </summary>
    internal const string ApiKeyHashSecret = "integration-test-api-key-hash-secret-32+chars!";
    internal const string CredentialEncryptionKey = "integration-test-credential-encryption-key-32+";
    internal const string WebhookSecretEncryptionKey = "integration-test-webhook-secret-key-32+chars!";

    /// <summary>
    /// Environment name we force on the host. With <c>Bootstrap:Enabled=false</c>
    /// the seeder is a no-op, but setting <c>Production</c> also flips
    /// <see cref="PaymentHub.Application.Abstractions.Context.IRuntimeEnvironment.IsDevelopment"/>
    /// to false so behaviour matches production paths.
    /// </summary>
    internal const string TestEnvironment = "Production";

    private readonly PostgresFixture _fixture;
    private readonly AbacatePayFakeHttpHandler _abacateHandler;
    private readonly ApplicationWebhookCaptureHandler _webhookHandler;

    public PaymentHubApiFactory(PostgresFixture fixture)
    {
        _fixture = fixture;
        _abacateHandler = new AbacatePayFakeHttpHandler();
        _webhookHandler = new ApplicationWebhookCaptureHandler();
    }

    /// <summary>
    /// Fake transport the API uses for outbound AbacatePay calls.
    /// Tests inspect <c>LastRequestBody</c> / <c>CallCount</c> to assert
    /// what the adapter actually sent.
    /// </summary>
    public AbacatePayFakeHttpHandler AbacatePayHandler => _abacateHandler;

    /// <summary>
    /// Fake transport the API uses for outbound webhook deliveries.
    /// Tests inspect <c>Captured</c> to assert dispatch behaviour without
    /// running the real <c>OutboxDispatcherWorker</c>.
    /// </summary>
    public ApplicationWebhookCaptureHandler WebhookHandler => _webhookHandler;

    /// <summary>
    /// Returns the protected credential blob that, when unprotected with
    /// <see cref="AesCredentialProtector"/> (the same key the API uses),
    /// yields the JSON <c>{ "apiKey": "...", "webhookSecret": "..." }</c>
    /// expected by the AbacatePay adapter.
    /// </summary>
    public string ProtectAbacatePayCredentials(string apiKey, string webhookSecret)
    {
        using var scope = Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<PaymentHub.Application.Abstractions.Security.ICredentialProtector>();
        var json = $"{{\"apiKey\":\"{apiKey}\",\"webhookSecret\":\"{webhookSecret}\"}}";
        return protector.Protect(json);
    }

    /// <summary>
    /// Returns the protected webhook secret blob the
    /// <see cref="PaymentHub.Application.Abstractions.Security.IWebhookSecretProtector"/>
    /// would persist alongside the <c>ApplicationClient</c>. The API host's
    /// protector is configured with the deterministic 32-byte key the factory
    /// ships in <see cref="WebhookSecretEncryptionKey"/>.
    /// </summary>
    public string ProtectWebhookSecret(string plainSecret)
    {
        using var scope = Services.CreateScope();
        var protector = scope.ServiceProvider
            .GetRequiredService<PaymentHub.Application.Abstractions.Security.IWebhookSecretProtector>();
        return protector.Protect(plainSecret);
    }

    /// <summary>
    /// Computes the HMAC-SHA256 hash that the API key middleware expects
    /// to see in the database for the given plain API key.
    /// </summary>
    public string HashApiKey(string plainApiKey)
    {
        using var scope = Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<PaymentHub.Application.Abstractions.Security.IApiKeyHasher>();
        return hasher.Hash(plainApiKey);
    }

    /// <summary>
    /// Truncates every application table in topological order. Mirrors the
    /// behaviour of <c>IntegrationTestFactory.ResetDatabaseAsync</c> so the
    /// two factories share the same isolation guarantees.
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

        await using var context = new PaymentHubDbContext(new DbContextOptionsBuilder<PaymentHubDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(PaymentHubDbContext).Assembly.GetName().Name))
            .Options);
        await context.Database.ExecuteSqlRawAsync(truncateSql);
    }

    /// <summary>
    /// Opens a fresh DI scope and resolves a <see cref="PaymentHubDbContext"/>
    /// pointing at the Testcontainers Postgres. Tests use this to assert
    /// post-conditions (created payments, persisted attempts, etc.).
    /// </summary>
    public PaymentHubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentHubDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(PaymentHubDbContext).Assembly.GetName().Name))
            .Options;
        return new PaymentHubDbContext(options);
    }

    /// <summary>
    /// Opens a fresh DI scope and resolves the requested service. Useful
    /// when tests want to call handlers directly (e.g. simulate the
    /// <c>WebhookProcessorWorker</c> tick via
    /// <see cref="PaymentHub.Application.Webhooks.IProcessWebhookEventHandler"/>).
    /// </summary>
    public T ResolveScoped<T>() where T : notnull
    {
        var scope = Services.CreateScope();
        try
        {
            return scope.ServiceProvider.GetRequiredService<T>();
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Resolves a scoped service via a synchronous <see cref="IServiceScope"/>.
    /// Returns both the resolved service and the scope so the caller can
    /// dispose it explicitly when async disposal is not needed.
    /// </summary>
    public (T Service, IServiceScope Scope) ResolveScopedWithLifetime<T>() where T : notnull
    {
        var scope = Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<T>();
        return (service, scope);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnvironment);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                // Postgres points at the Testcontainers container, not the
                // docker-compose service used by appsettings.Development.json.
                ["ConnectionStrings:Postgres"] = _fixture.ConnectionString,

                // Crypto secrets: same deterministic values used by the
                // Slice 1-IT integration fixture so protectors accept them.
                ["PaymentHub:ApiKeyHashSecret"] = ApiKeyHashSecret,
                ["PaymentHub:CredentialEncryptionKey"] = CredentialEncryptionKey,
                ["PaymentHub:WebhookSecretEncryptionKey"] = WebhookSecretEncryptionKey,

                // Default provider wired to AbacatePay so the E2E flow
                // exercises the real adapter. Individual tests override
                // X-Provider when they want the Fake adapter instead.
                ["PaymentHub:DefaultProvider"] = "AbacatePay",

                // AbacatePay outbound config — base URL is arbitrary because
                // the transport is replaced by AbacatePayFakeHttpHandler,
                // but the value still has to be a valid Uri for the
                // HttpClient to build successfully.
                ["Providers:AbacatePay:BaseUrl"] = "https://abacatepay.fake/v2",
                ["Providers:AbacatePay:TimeoutSeconds"] = "5",
                ["Providers:AbacatePay:AllowDevModeSimulation"] = "true",

                // Bootstrap seeder is OFF in every test. The Slice 6-D
                // policy enforces this — without it the dev tenant would
                // be created automatically.
                ["Bootstrap:Enabled"] = "false",
                ["Bootstrap:SeedDevelopmentData"] = "false",
                ["Bootstrap:AllowProductionBootstrap"] = "false",
                ["Bootstrap:DevelopmentTenantSlug"] = "it-tenant",
                ["Bootstrap:DevelopmentApplicationName"] = "it-app"
            };
            config.AddInMemoryCollection(overrides);
        });

builder.ConfigureTestServices(services =>
        {
            // Re-register the named HttpClients so their primary message
            // handler becomes our fake. The original registration
            // (which sets BaseAddress/Timeout) still runs first, then
            // our additional ConfigurePrimaryHttpMessageHandler wins
            // because HttpMessageHandlerBuilderActions is executed in
            // registration order and the last PrimaryHandler assignment
            // takes effect.
            services.AddHttpClient(PaymentHub.Infrastructure.Providers.AbacatePay.AbacatePayClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _abacateHandler);

            services.AddHttpClient(PostgresServiceCollectionExtensions.ApplicationWebhookHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _webhookHandler);
        });
    }

    /// <summary>
    /// Overrides <see cref="WebApplicationFactory{TEntryPoint}.CreateHost"/>
    /// to install a <see cref="IHostBuilder.ConfigureHostConfiguration"/>
    /// source BEFORE the WebApplicationBuilder reads the configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this is necessary: <c>Program.cs</c> calls
    /// <c>AddPaymentHubPostgres(builder.Configuration)</c> eagerly while
    /// <c>WebApplication.CreateBuilder(args)</c> is still in scope, which
    /// is BEFORE the host is built and BEFORE the
    /// <see cref="ConfigureWebHost"/> <c>ConfigureAppConfiguration</c>
    /// callback fires. A normal <c>ConfigureAppConfiguration</c> override
    /// therefore arrives too late to override the connection string.
    /// </para>
    /// <para>
    /// Hooking <see cref="IHostBuilder.ConfigureHostConfiguration"/>
    /// instead installs the in-memory source at the host level, where it
    /// becomes available to the <see cref="WebApplicationBuilder.Configuration"/>
    /// live <see cref="ConfigurationManager"/> before the user code runs.
    /// </para>
    /// </remarks>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Postgres connection string MUST be wired before the host
                // is built so Program.cs sees the test container, not the
                // docker-compose service defined in appsettings.json.
                ["ConnectionStrings:Postgres"] = _fixture.ConnectionString,
                ["PaymentHub:ApiKeyHashSecret"] = ApiKeyHashSecret,
                ["PaymentHub:CredentialEncryptionKey"] = CredentialEncryptionKey,
                ["PaymentHub:WebhookSecretEncryptionKey"] = WebhookSecretEncryptionKey,
                ["PaymentHub:DefaultProvider"] = "AbacatePay",
                ["Providers:AbacatePay:BaseUrl"] = "https://abacatepay.fake/v2",
                ["Providers:AbacatePay:TimeoutSeconds"] = "5",
                ["Providers:AbacatePay:AllowDevModeSimulation"] = "true",
                ["Bootstrap:Enabled"] = "false",
                ["Bootstrap:SeedDevelopmentData"] = "false",
                ["Bootstrap:AllowProductionBootstrap"] = "false",
                ["Bootstrap:DevelopmentTenantSlug"] = "it-tenant",
                ["Bootstrap:DevelopmentApplicationName"] = "it-app"
            });
        });

        return base.CreateHost(builder);
    }
}