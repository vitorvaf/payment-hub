using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentHub.IntegrationTests.Persistence;

/// <summary>
/// End-to-end persistence verification for the two central aggregates of
/// the multitenancy model: <see cref="Tenant"/> and <see cref="ApplicationClient"/>.
/// Each test creates the entity, persists it, disposes the DbContext, reloads
/// via a fresh context, and asserts the round-tripped state is identical.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TenantApplicationClientPersistenceTests
{
    private readonly IntegrationTestFactory _factory;

    public TenantApplicationClientPersistenceTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
        _factory.ResetDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task DbContext_ShouldPersistTenantAndApplicationClient_AndReloadCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        // Seed via the repository abstraction (covers the same path the
        // production code uses: add + SaveChangesAsync).
        var tenant = await _factory.SeedTenantAsync(
            id: tenantId,
            name: "Acme 1IT",
            slug: "acme-1it");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenantId,
            id: applicationId,
            name: "App 1IT");

        // Open a brand new DbContext to force a real SELECT (not change tracking).
        using var scope = _factory.CreateScope();
        var tenantRepo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var appRepo = scope.ServiceProvider.GetRequiredService<IApplicationClientRepository>();

        var reloadedTenant = await tenantRepo.GetByIdAsync(tenantId, CancellationToken.None);
        var reloadedApp = await appRepo.GetByTenantAndIdAsync(tenantId, applicationId, CancellationToken.None);

        reloadedTenant.Should().NotBeNull();
        reloadedApp.Should().NotBeNull();

        reloadedTenant!.Id.Should().Be(tenantId);
        reloadedTenant.Name.Should().Be("Acme 1IT");
        reloadedTenant.Slug.Should().Be("acme-1it");
        reloadedTenant.Status.Should().Be(TenantStatus.Active);
        reloadedTenant.CreatedAt.Should().NotBe(default);
        reloadedTenant.UpdatedAt.Should().NotBe(default);
        reloadedTenant.CreatedAt.Should().BeCloseTo(
            reloadedTenant.UpdatedAt, TimeSpan.FromSeconds(1));

        reloadedApp!.Id.Should().Be(applicationId);
        reloadedApp.TenantId.Should().Be(tenantId);
        reloadedApp.Name.Should().Be("App 1IT");
        reloadedApp.Status.Should().Be(ApplicationStatus.Active);
        reloadedApp.HasWebhookSecret.Should().BeFalse();
        reloadedApp.WebhookUrl.Should().BeNull();
    }

    [Fact]
    public async Task Tenant_AndApplication_UniqueIndex_ShouldPreventDuplicateSlug()
    {
        await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme Original",
            slug: "acme-unique");

        // Trying to insert a second tenant with the same slug must violate the
        // unique index and surface as a DbUpdateException.
        using var scope = _factory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var duplicate = new Tenant(Guid.NewGuid(), "Acme Duplicate", "acme-unique");
        await repo.AddAsync(duplicate, CancellationToken.None);

        var act = async () => await uow.SaveChangesAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateException>(
            because: "the tenants.slug unique index must reject duplicates");
    }
}
