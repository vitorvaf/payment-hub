using FluentAssertions;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Enums;
using PaymentHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentHub.IntegrationTests.Persistence;

/// <summary>
/// Verifies that <c>ProviderAccount</c> persists cleanly, that the default
/// and by-code queries both return the persisted row, and that the
/// tenant/application scope guard rejects queries for unrelated tenants.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ProviderAccountPersistenceTests
{
    private readonly IntegrationTestFactory _factory;

    public ProviderAccountPersistenceTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
        _factory.ResetDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ProviderAccountRepository_ShouldPersistAndLoadByTenantAndApplication()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme Provider",
            slug: "acme-provider-1it");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "App Provider 1IT");

        var seeded = await _factory.SeedProviderAccountAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            providerCode: ProviderCode.Fake,
            environment: ProviderEnvironment.Sandbox,
            name: "Acme Fake Sandbox",
            encryptedCredentials: "encrypted-fake-credentials-placeholder",
            isDefault: true);

        using var scope = _factory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();

        var byDefault = await repo.GetDefaultAsync(
            tenant.Id, application.Id, ProviderCode.Fake, CancellationToken.None);
        byDefault.Should().NotBeNull();
        byDefault!.Id.Should().Be(seeded.Id);
        byDefault.TenantId.Should().Be(tenant.Id);
        byDefault.ApplicationId.Should().Be(application.Id);
        byDefault.ProviderCode.Should().Be(ProviderCode.Fake);
        byDefault.Environment.Should().Be(ProviderEnvironment.Sandbox);
        byDefault.IsDefault.Should().BeTrue();
        byDefault.Active.Should().BeTrue();
        byDefault.Name.Should().Be("Acme Fake Sandbox");

        var byCode = await repo.GetByCodeAsync(
            tenant.Id, application.Id, ProviderCode.Fake, CancellationToken.None);
        byCode.Should().NotBeNull();
        byCode!.Id.Should().Be(seeded.Id);
        byCode.IsDefault.Should().BeTrue();
        byCode.Active.Should().BeTrue();
    }

    [Fact]
    public async Task ProviderAccountRepository_ShouldRespectsTenantScope()
    {
        var tenantA = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme A",
            slug: "acme-a-1it");
        var applicationA = await _factory.SeedApplicationClientAsync(
            tenantId: tenantA.Id,
            id: Guid.NewGuid(),
            name: "App A 1IT");

        var tenantB = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme B",
            slug: "acme-b-1it");

        await _factory.SeedProviderAccountAsync(
            tenantId: tenantA.Id,
            applicationId: applicationA.Id,
            providerCode: ProviderCode.Fake,
            environment: ProviderEnvironment.Sandbox,
            name: "Acme A Fake Sandbox");

        using var scope = _factory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProviderAccountRepository>();

        // Asking for the default account under tenant B's scope must return null
        // even though a default account exists for tenant A.
        var defaultForTenantB = await repo.GetDefaultAsync(
            tenantB.Id, applicationA.Id, ProviderCode.Fake, CancellationToken.None);
        defaultForTenantB.Should().BeNull(
            because: "the repository query is scoped by tenant id and must not leak across tenants");

        var byCodeForTenantB = await repo.GetByCodeAsync(
            tenantB.Id, applicationA.Id, ProviderCode.Fake, CancellationToken.None);
        byCodeForTenantB.Should().BeNull();
    }
}
