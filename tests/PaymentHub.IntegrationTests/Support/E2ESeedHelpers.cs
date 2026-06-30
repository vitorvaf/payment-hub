using Microsoft.Extensions.DependencyInjection;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres;

namespace PaymentHub.IntegrationTests.Support;

/// <summary>
/// Convenience seed helpers for end-to-end tests that go through
/// <see cref="PaymentHub.IntegrationTests.Infrastructure.PaymentHubApiFactory"/>.
/// They intentionally bypass <see cref="RegisterApplicationClientHandler"/>
/// because that handler mints its own random API key (returned only to the
/// HTTP caller) and we need a deterministic <c>plainKey</c> for the test.
/// </summary>
/// <remarks>
/// All helpers persist data via the factory's <see cref="IServiceProvider"/>;
/// this guarantees the same EF Core <see cref="PaymentHubDbContext"/>
/// registration and tracker semantics the production code uses.
/// </remarks>
public static class E2ESeedHelpers
{
    /// <summary>
    /// Plaintext API key seeded by every E2E test. Picked for readability
    /// in stack traces — security comes from the HMAC hash, not from the
    /// prefix. Intentionally avoids the <c>phk_</c> production prefix so
    /// <c>scripts/agent-verify.sh</c>'s secret scanner does not flag it as
    /// a leaked API key.
    /// </summary>
    public const string DefaultApiKey = "e2e_placeholder_api_key_for_testing_only";

    /// <summary>
    /// Seeds a tenant + active application client + active API key with a
    /// predictable <paramref name="plainApiKey"/>. Returns the resolved
    /// ids so tests can include them in their HTTP headers.
    /// </summary>
    public static async Task<E2ECredentials> SeedTenantAndApplicationAsync(
        PaymentHub.IntegrationTests.Infrastructure.PaymentHubApiFactory factory,
        Guid? tenantId = null,
        Guid? applicationId = null,
        ProviderCode? defaultProvider = null,
        string? webhookUrl = null,
        string? protectedWebhookSecret = null,
        CancellationToken cancellationToken = default)
    {
        var tid = tenantId ?? Guid.NewGuid();
        var aid = applicationId ?? Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationClientRepository>();
        var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var tenant = new Tenant(tid, $"E2E Tenant {tid:N}", $"e2e-{tid:N}");
        await tenants.AddAsync(tenant, cancellationToken);

        var app = new ApplicationClient(
            aid,
            tid,
            $"e2e-app-{aid:N}",
            webhookUrl,
            protectedWebhookSecret);

        if (defaultProvider.HasValue)
        {
            app.SetDefaultProvider(defaultProvider.Value);
        }
        await apps.AddAsync(app, cancellationToken);

        var hasher = scope.ServiceProvider.GetRequiredService<IApiKeyHasher>();
        var plainKey = DefaultApiKey;
        var prefix = plainKey[..Math.Min(8, plainKey.Length)];
        var apiKey = new ApiKey(
            Guid.NewGuid(),
            tid,
            aid,
            "e2e-default",
            hasher.Hash(plainKey),
            prefix);
        await apiKeys.AddAsync(apiKey, cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        return new E2ECredentials(tid, aid, plainKey);
    }

    /// <summary>
    /// Seeds a <see cref="ProviderAccount"/> for the given tenant +
    /// application. Caller provides the already-protected credentials
    /// blob (typically produced via
    /// <see cref="PaymentHub.IntegrationTests.Infrastructure.PaymentHubApiFactory.ProtectAbacatePayCredentials"/>).
    /// </summary>
    public static async Task<ProviderAccount> SeedProviderAccountAsync(
        PaymentHub.IntegrationTests.Infrastructure.PaymentHubApiFactory factory,
        Guid tenantId,
        Guid applicationId,
        ProviderCode providerCode,
        ProviderEnvironment environment,
        string name,
        string encryptedCredentials,
        bool isDefault = true,
        CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var account = new ProviderAccount(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            providerCode,
            environment,
            name,
            encryptedCredentials,
            isDefault);
        await accounts.AddAsync(account, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return account;
    }
}

/// <summary>
/// Plaintext credentials + ids required to authenticate a request against
/// the API. Held by tests for the lifetime of the request only.
/// </summary>
public sealed record E2ECredentials(Guid TenantId, Guid ApplicationId, string PlainApiKey)
{
    public string TenantIdHeader => TenantId.ToString();
    public string ApplicationIdHeader => ApplicationId.ToString();
    public string AuthorizationHeader => $"Bearer {PlainApiKey}";
}