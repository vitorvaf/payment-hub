using FluentAssertions;
using Moq;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Application;

/// <summary>
/// Unit tests for the Slice 2-C <c>GetProviderAccountWebhookHandler</c>.
/// Covers scope guards (404 / 409 / unsupported), payload assembly and
/// the contract that the response NEVER carries secret material.
/// </summary>
public class GetProviderAccountWebhookHandlerTests
{
    private readonly Mock<IProviderAccountRepository> _accounts = new(MockBehavior.Strict);
    private readonly FakeCredentialProtector _protector = new();

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenTenantIdIsEmpty()
    {
        var handler = CreateHandler();
        var act = async () => await handler.HandleAsync(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tenant*");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenApplicationIdIsEmpty()
    {
        var handler = CreateHandler();
        var act = async () => await handler.HandleAsync(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*application*");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenProviderAccountIdIsEmpty()
    {
        var handler = CreateHandler();
        var act = async () => await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ProviderAccount id*");
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNotFound_WhenAccountMissing()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _accounts.Setup(a => a.GetByIdForTenantAndApplicationAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderAccount?)null);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, CancellationToken.None);

        outcome.Should().BeOfType<GetWebhookOutcome.NotFound>();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnInactive_WhenAccountIsInactive()
    {
        var (tenantId, applicationId, providerAccountId, _) = SeedAbacatePay(active: false);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, CancellationToken.None);

        outcome.Should().BeOfType<GetWebhookOutcome.Inactive>();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnUnsupportedProvider_WhenAccountIsNotAbacatePay()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        var account = new ProviderAccount(
            providerAccountId,
            tenantId,
            applicationId,
            ProviderCode.Fake,
            ProviderEnvironment.Sandbox,
            "fake",
            _protector.Protect("{\"apiKey\":\"k\"}"));
        _accounts.Setup(a => a.GetByIdForTenantAndApplicationAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, CancellationToken.None);

        outcome.Should().BeOfType<GetWebhookOutcome.UnsupportedProvider>();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnSuccess_WithExistingWebhookConfig()
    {
        var (tenantId, applicationId, providerAccountId, accountId) = SeedAbacatePay(
            active: true,
            apiKey: "sk_test_abc",
            webhookSecret: "stored-webhook-secret-123");
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, CancellationToken.None);

        outcome.Should().BeOfType<GetWebhookOutcome.Success>();
        var success = (GetWebhookOutcome.Success)outcome;
        success.Response.ProviderAccountId.Should().Be(accountId);
        success.Response.ProviderCode.Should().Be(ProviderCode.AbacatePay);
        success.Response.HasWebhookSecret.Should().BeTrue();
        success.Response.CallbackUrl.Should().BeNull();
        success.Response.Events.Should().BeEmpty();
        success.Response.RemoteRegistrationStatus.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnHasWebhookSecretFalse_WhenCredentialsHaveNoSecret()
    {
        var (tenantId, applicationId, providerAccountId, _) = SeedAbacatePay(
            active: true,
            apiKey: "sk_test_abc",
            webhookSecret: null);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, CancellationToken.None);

        outcome.Should().BeOfType<GetWebhookOutcome.Success>();
        var success = (GetWebhookOutcome.Success)outcome;
        success.Response.HasWebhookSecret.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldNeverExposeSecretMaterial_InResponseType()
    {
        var dtoType = typeof(ProviderAccountWebhookResponseDto);
        dtoType.GetProperty("ApiKey").Should().BeNull();
        dtoType.GetProperty("WebhookSecret").Should().BeNull();
        dtoType.GetProperty("ProtectedWebhookSecret").Should().BeNull();
        dtoType.GetProperty("EncryptedCredentials").Should().BeNull();
        dtoType.GetProperty("HasWebhookSecret").Should().NotBeNull(
            "response should expose a boolean flag for webhook secret presence");
    }

    // ---- helpers ----

    private GetProviderAccountWebhookHandler CreateHandler()
        => new(_accounts.Object, _protector);

    private (Guid TenantId, Guid ApplicationId, Guid ProviderAccountId, Guid AccountId)
        SeedAbacatePay(
            bool active,
            string apiKey = "sk_test_seed",
            string? webhookSecret = "seed-webhook-secret-1234567890")
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        var json = webhookSecret is null
            ? System.Text.Json.JsonSerializer.Serialize(new { apiKey })
            : System.Text.Json.JsonSerializer.Serialize(new { apiKey, webhookSecret });
        var account = new ProviderAccount(
            providerAccountId,
            tenantId,
            applicationId,
            ProviderCode.AbacatePay,
            ProviderEnvironment.Sandbox,
            "seed-abacate",
            _protector.Protect(json));
        if (!active) account.Disable();

        _accounts.Setup(a => a.GetByIdForTenantAndApplicationAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        return (tenantId, applicationId, providerAccountId, account.Id);
    }
}
