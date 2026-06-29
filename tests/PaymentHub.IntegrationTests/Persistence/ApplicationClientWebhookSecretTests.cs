using FluentAssertions;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentHub.IntegrationTests.Persistence;

/// <summary>
/// Validates the end-to-end protection contract for <c>ApplicationClient.WebhookSecret</c>:
/// raw plaintext MUST NOT be persisted, the stored blob MUST be decryptable
/// via <see cref="IWebhookSecretProtector.Unprotect"/>, and the metadata
/// surface (<c>HasWebhookSecret</c>) reflects the presence of a secret.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ApplicationClientWebhookSecretTests
{
    private const string RawWebhookSecret = "raw-webhook-secret-1it-do-not-log";

    private readonly IntegrationTestFactory _factory;
    private readonly IWebhookSecretProtector _protector;

    public ApplicationClientWebhookSecretTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
        _protector = _factory.CreateWebhookSecretProtector();
        _factory.ResetDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ApplicationClient_ShouldPersistProtectedWebhookSecret_AndAllowInternalUnprotect()
    {
        // Arrange: tenant + protected webhook secret (caller is the API layer;
        // we replicate the contract here instead of going through the HTTP host).
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme WebhookSecret",
            slug: "acme-wh-1it");

        var protectedSecret = _protector.Protect(RawWebhookSecret);

        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "App WH 1IT",
            protectedWebhookSecret: protectedSecret);

        application.HasWebhookSecret.Should().BeTrue();

        // Act: reload via a fresh DbContext to confirm we read from disk.
        using var scope = _factory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IApplicationClientRepository>();
        var reloaded = await repo.GetByTenantAndIdAsync(tenant.Id, application.Id, CancellationToken.None);

        // Assert: stored value is NOT the plaintext, is NOT prefixed with
        // "whsec_" (we never store plaintext), and is decryptable by the
        // protector that was configured for this fixture.
        reloaded.Should().NotBeNull();
        reloaded!.WebhookSecret.Should().NotBeNullOrWhiteSpace();
        reloaded.WebhookSecret.Should().NotBe(RawWebhookSecret,
            because: "plaintext secret must never be persisted");
        reloaded.WebhookSecret!.StartsWith("whsec_").Should().BeFalse(
            because: "the database column holds an opaque AES-CBC blob, not a plaintext marker");

        var unprotected = _protector.Unprotect(reloaded.WebhookSecret!);
        unprotected.Should().Be(RawWebhookSecret,
            because: "the protector roundtrip must recover the original secret");

        reloaded.HasWebhookSecret.Should().BeTrue();
    }

    [Fact]
    public async Task ApplicationClient_WithoutWebhookSecret_ShouldReportHasWebhookSecretFalse()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme NoSecret",
            slug: "acme-nosecret-1it");

        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "App NoSecret 1IT");

        application.HasWebhookSecret.Should().BeFalse();

        using var scope = _factory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IApplicationClientRepository>();
        var reloaded = await repo.GetByTenantAndIdAsync(tenant.Id, application.Id, CancellationToken.None);

        reloaded.Should().NotBeNull();
        reloaded!.WebhookSecret.Should().BeNull();
        reloaded.HasWebhookSecret.Should().BeFalse();
    }
}
