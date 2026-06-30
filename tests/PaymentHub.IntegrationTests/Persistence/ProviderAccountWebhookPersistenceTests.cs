using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.IntegrationTests.Infrastructure;

namespace PaymentHub.IntegrationTests.Persistence;

/// <summary>
/// Slice 2-C integration tests — verifies that the four non-sensitive
/// webhook columns on <c>provider_accounts</c>
/// (<c>webhook_callback_url</c>, <c>webhook_events</c>,
/// <c>webhook_configured_at</c>, <c>webhook_remote_status</c>)
/// persist correctly via EF Core and the auto-applied migration, and that
/// the round-tripped response shape NEVER leaks
/// <c>apiKey</c>, <c>secret</c>, <c>webhookSecret</c> or
/// <c>EncryptedCredentials</c> even when those fields are populated.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ProviderAccountWebhookPersistenceTests
{
    private readonly IntegrationTestFactory _factory;

    public ProviderAccountWebhookPersistenceTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
        _factory.ResetDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ProviderAccount_ShouldPersistAllWebhookConfigurationColumns()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Webhook-Persistence Tenant",
            slug: "webhook-persistence-tenant");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "Webhook-Persistence App");

        // Use real AES protector so we exercise both the credentials
        // blob round-trip AND the webhook columns in one go.
        var protector = _factory.CreateScope().ServiceProvider.GetRequiredService<ICredentialProtector>();
        var initialCredentials = "{ \"apiKey\": \"sk_test_persistence\", \"webhookSecret\": \"initial-webhook-secret\" }";
        var protectedCredentials = protector.Protect(initialCredentials);

        var seeded = await _factory.SeedProviderAccountAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            providerCode: ProviderCode.AbacatePay,
            environment: ProviderEnvironment.Sandbox,
            name: "Acme Abacate Sandbox",
            encryptedCredentials: protectedCredentials,
            isDefault: true);

        // Configure the webhook on the entity and persist.
        var eventsJson = "[\"transparent.completed\",\"transparent.refunded\"]";
        seeded.ConfigureWebhook(
            callbackUrl: "https://merchant.example.com/webhook",
            eventsJson: eventsJson,
            remoteStatus: ProviderWebhookRemoteStatus.RemoteRegistrationDeferred);

        using (var scope = _factory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.UpdateAsync(seeded, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        // Reload and assert every column round-trips through Postgres.
        using (var scope = _factory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
            var reloaded = await repo.GetByCodeAsync(
                tenant.Id, application.Id, ProviderCode.AbacatePay, CancellationToken.None);

            reloaded.Should().NotBeNull();
            reloaded!.WebhookCallbackUrl.Should().Be("https://merchant.example.com/webhook");
            reloaded.WebhookEvents.Should().Be(eventsJson);
            reloaded.WebhookRemoteStatus.Should().Be(ProviderWebhookRemoteStatus.RemoteRegistrationDeferred);
            reloaded.WebhookConfiguredAt.Should().NotBeNull();
            reloaded.WebhookConfiguredAt!.Value.Should().BeCloseTo(
                DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task ProviderAccount_GetWebhookResponse_ShouldNeverExposeSensitiveMaterial()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Webhook-Response Tenant",
            slug: "webhook-response-tenant");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "Webhook-Response App");

        var protector = _factory.CreateScope().ServiceProvider.GetRequiredService<ICredentialProtector>();
        var initialCredentials = "{ \"apiKey\": \"sk_test_abcdefghij\", \"webhookSecret\": \"secret-webhook-value\" }";
        var protectedCredentials = protector.Protect(initialCredentials);

        var account = await _factory.SeedProviderAccountAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            providerCode: ProviderCode.AbacatePay,
            environment: ProviderEnvironment.Production,
            name: "Acme Abacate Production",
            encryptedCredentials: protectedCredentials,
            isDefault: true);

        // Drive the entity to populate its webhook columns.
        account.ConfigureWebhook(
            callbackUrl: "https://merchant.example.com/hooks",
            eventsJson: "[\"transparent.completed\"]",
            remoteStatus: ProviderWebhookRemoteStatus.Registered);
        using (var scope = _factory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.UpdateAsync(account, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        // Now exercise the Get handler so we can assert the response
        // shape. The handler is in the Application layer; we manually
        // wire its dependencies using the integration fixture's
        // scope-creator so we don't have to spin up the full API host.
        using var scope2 = _factory.CreateScope();
        var sp = scope2.ServiceProvider;
        var accounts = sp.GetRequiredService<IProviderAccountRepository>();
        var newProtector = sp.GetRequiredService<ICredentialProtector>();
        var getHandler = new GetProviderAccountWebhookHandler(accounts, newProtector);

        var outcome = await getHandler.HandleAsync(
            tenant.Id, application.Id, account.Id, CancellationToken.None);

        outcome.Should().BeOfType<GetWebhookOutcome.Success>();
        var success = (GetWebhookOutcome.Success)outcome;

        // The response carries `hasWebhookSecret=true` because the
        // underlying blob has a secret field — but the secret value
        // itself is never returned.
        success.Response.HasWebhookSecret.Should().BeTrue();
        success.Response.CallbackUrl.Should().Be("https://merchant.example.com/hooks");
        success.Response.Events.Should().BeEquivalentTo(new[] { "transparent.completed" });
        success.Response.RemoteRegistrationStatus.Should().Be(nameof(ProviderWebhookRemoteStatus.Registered));

        // Reflection-level assertion that no sensitive field is on the
        // response DTO — same shape as the unit test, repeated here as
        // the integration check.
        var dtoType = typeof(ProviderAccountWebhookResponseDto);
        dtoType.GetProperty("ApiKey").Should().BeNull();
        dtoType.GetProperty("WebhookSecret").Should().BeNull();
        dtoType.GetProperty("ProtectedWebhookSecret").Should().BeNull();
        dtoType.GetProperty("EncryptedCredentials").Should().BeNull();
    }

    [Fact]
    public async Task ProviderAccount_ConfigureWebhook_ShouldResetColumns_WhenCleared()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Webhook-Clear Tenant",
            slug: "webhook-clear-tenant");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "Webhook-Clear App");

        var account = await _factory.SeedProviderAccountAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            providerCode: ProviderCode.AbacatePay,
            environment: ProviderEnvironment.Sandbox,
            name: "Acme Clear Sandbox",
            encryptedCredentials: "encrypted-stub",
            isDefault: true);

        // First, set some configuration.
        account.ConfigureWebhook(
            callbackUrl: "https://first.example.com/hook",
            eventsJson: "[\"transparent.disputed\"]",
            remoteStatus: ProviderWebhookRemoteStatus.RegistrationFailed);
        using (var scope = _factory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.UpdateAsync(account, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        // Now clear it.
        account.ConfigureWebhook(
            callbackUrl: null,
            eventsJson: null,
            remoteStatus: ProviderWebhookRemoteStatus.NotRegistered);
        using (var scope = _factory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.UpdateAsync(account, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        // Reload and assert everything cleared.
        using var scope2 = _factory.CreateScope();
        var accounts = scope2.ServiceProvider.GetRequiredService<IProviderAccountRepository>();
        var reloaded = await accounts.GetByCodeAsync(
            tenant.Id, application.Id, ProviderCode.AbacatePay, CancellationToken.None);

        reloaded.Should().NotBeNull();
        reloaded!.WebhookCallbackUrl.Should().BeNull();
        reloaded.WebhookEvents.Should().BeNull();
        reloaded.WebhookRemoteStatus.Should().Be(ProviderWebhookRemoteStatus.NotRegistered);
        reloaded.WebhookConfiguredAt.Should().NotBeNull(
            "the configured_at timestamp records when the LAST write happened, including clears");
    }
}
