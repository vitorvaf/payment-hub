using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Application;

/// <summary>
/// Unit tests for the Slice 2-C
/// <c>ConfigureProviderAccountWebhookHandler</c>. Covers credentials
/// preservation (apiKey is never dropped, <c>webhookSecret</c> follows
/// the documented rules), scope guards (404 vs 409 vs unsupported
/// provider), and the remote-registration gate behaviour.
/// </summary>
public class ConfigureProviderAccountWebhookHandlerTests
{
    private readonly Mock<IProviderAccountRepository> _accounts = new(MockBehavior.Strict);
    private readonly FakeCredentialProtector _protector = new();
    private readonly Mock<IUnitOfWork> _uow = new(MockBehavior.Strict);
    private readonly FakeProviderWebhookManagementClient _client = new();
    private readonly FakeProviderWebhookRegistrationFeaturePolicy _featurePolicy = new();
    private readonly NullLogger<ConfigureProviderAccountWebhookHandler> _logger = new();

    public ConfigureProviderAccountWebhookHandlerTests()
    {
        _accounts.Setup(a => a.UpdateAsync(It.IsAny<ProviderAccount>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenTenantIdIsEmpty()
    {
        var handler = CreateHandler();
        var request = ValidRequest();
        var act = async () => await handler.HandleAsync(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tenant*");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenApplicationIdIsEmpty()
    {
        var handler = CreateHandler();
        var request = ValidRequest();
        var act = async () => await handler.HandleAsync(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*application*");
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenProviderAccountIdIsEmpty()
    {
        var handler = CreateHandler();
        var request = ValidRequest();
        var act = async () => await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ProviderAccount id is required*");
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNotFound_WhenAccountMissingInCallerScope()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _accounts.Setup(a => a.GetByIdForTenantAndApplicationAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderAccount?)null);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, ValidRequest(), CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.NotFound>();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnInactive_WhenAccountIsInactive()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: false);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, ValidRequest(), CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Inactive>();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnUnsupportedProvider_WhenAccountIsNotAbacatePay()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedFakeAccount();
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, ValidRequest(), CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.UnsupportedProvider>();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldPreserveApiKey_WhenUpdatingWebhookSecret()
    {
        var (tenantId, applicationId, providerAccountId, accountId) = await SeedAbacatePayAccount(
            active: true,
            apiKey: "sk_test_abc",
            webhookSecret: "old-webhook-secret");

        var handler = CreateHandler();

        var request = ValidRequest() with { WebhookSecret = "new-webhook-secret" };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        var success = (ConfigureWebhookOutcome.Success)outcome;

        // Inspect the protected blob the handler persisted. The persisted
        // entity is captured by the UpdateAsync mock.
        var persisted = (ProviderAccount)_accounts.Invocations
            .First(i => i.Method.Name == "UpdateAsync").Arguments[0]!;
        var plain = _protector.Unprotect(persisted.EncryptedCredentials);

        plain.Should().Contain("\"apiKey\":\"sk_test_abc\"",
            "handler must preserve apiKey when round-tripping credentials");
        plain.Should().Contain("\"webhookSecret\":\"new-webhook-secret\"",
            "handler must replace webhookSecret when supplied");
        plain.Should().NotContain("old-webhook-secret",
            "the previous webhookSecret value must NOT survive the update");

        success.Response.HasWebhookSecret.Should().BeTrue();
        success.Response.ProviderAccountId.Should().Be(accountId);
        success.Response.CallbackUrl.Should().Be(request.CallbackUrl);
        success.Response.Events.Should().BeEquivalentTo(request.Events);
    }

    [Fact]
    public async Task HandleAsync_ShouldKeepLegacySecret_WhenWebhookSecretNotSupplied()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(
            active: true,
            apiKey: "sk_test_abc",
            webhookSecret: null,
            legacySecret: "legacy-secret-9999");
        // Sanity: the inspector reads the legacy field when no
        // `webhookSecret` is present in the blob.

        var handler = CreateHandler();

        var request = ValidRequest() with { WebhookSecret = null };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        var persisted = (ProviderAccount)_accounts.Invocations
            .First(i => i.Method.Name == "UpdateAsync").Arguments[0]!;
        var plain = _protector.Unprotect(persisted.EncryptedCredentials);
        plain.Should().Contain("legacy-secret-9999");
        plain.Should().Contain("\"apiKey\":\"sk_test_abc\"");
    }

    [Fact]
    public async Task HandleAsync_ShouldNotCallRemoteClient_WhenRegisterRemotelyFalse()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: true);
        _featurePolicy.AllowRemoteRegistration = true;
        var handler = CreateHandler();

        var request = ValidRequest() with { WebhookSecret = "x".PadRight(20, 'y'), RegisterRemotely = false };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        _client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotCallRemoteClient_WhenFeaturePolicyIsOff()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: true);
        _featurePolicy.AllowRemoteRegistration = false;
        var handler = CreateHandler();

        var request = ValidRequest() with { WebhookSecret = "x".PadRight(20, 'y'), RegisterRemotely = true };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        _client.CallCount.Should().Be(0);
        // RemoteRegistrationDeferred is recorded when caller asked for
        // registration but the policy blocked it.
        var persisted = (ProviderAccount)_accounts.Invocations
            .Last(i => i.Method.Name == "UpdateAsync").Arguments[0]!;
        persisted.WebhookRemoteStatus.Should().Be(ProviderWebhookRemoteStatus.RemoteRegistrationDeferred);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotCallRemoteClient_WhenWebhookSecretNotProvided()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: true);
        _featurePolicy.AllowRemoteRegistration = true;
        var handler = CreateHandler();

        var request = ValidRequest() with { WebhookSecret = null, RegisterRemotely = true };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        _client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldCallRemoteClient_AndRecordRegistered_WhenAllGatesPass()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: true);
        _featurePolicy.AllowRemoteRegistration = true;
        var handler = CreateHandler();

        var request = ValidRequest() with
        {
            WebhookSecret = "x".PadRight(20, 'y'),
            RegisterRemotely = true
        };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        _client.CallCount.Should().Be(1);
        _client.LastProviderCode.Should().Be(ProviderCode.AbacatePay);
        _client.LastCallbackUrl.Should().Be(request.CallbackUrl);
        _client.LastEvents.Should().BeEquivalentTo(request.Events);

        var persisted = (ProviderAccount)_accounts.Invocations
            .Last(i => i.Method.Name == "UpdateAsync").Arguments[0]!;
        persisted.WebhookRemoteStatus.Should().Be(ProviderWebhookRemoteStatus.Registered);
    }

    [Fact]
    public async Task HandleAsync_ShouldRecordRegistrationFailed_WhenRemoteClientReturnsFailed()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: true);
        _featurePolicy.AllowRemoteRegistration = true;
        _client.NextOutcome = ProviderWebhookRegistrationOutcome.RegistrationFailed;
        var handler = CreateHandler();

        var request = ValidRequest() with
        {
            WebhookSecret = "x".PadRight(20, 'y'),
            RegisterRemotely = true
        };
        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        outcome.Should().BeOfType<ConfigureWebhookOutcome.Success>();
        var persisted = (ProviderAccount)_accounts.Invocations
            .Last(i => i.Method.Name == "UpdateAsync").Arguments[0]!;
        persisted.WebhookRemoteStatus.Should().Be(ProviderWebhookRemoteStatus.RegistrationFailed);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotReturnSecretMaterial_InSuccessResponse()
    {
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(active: true);
        var handler = CreateHandler();

        var outcome = await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, ValidRequest(), CancellationToken.None);

        var success = (ConfigureWebhookOutcome.Success)outcome;
        var dtoType = success.Response.GetType();
        dtoType.GetProperty("ApiKey").Should().BeNull();
        dtoType.GetProperty("EncryptedCredentials").Should().BeNull();
        dtoType.GetProperty("WebhookSecret").Should().BeNull();
        dtoType.GetProperty("ProtectedWebhookSecret").Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenCredentialsCannotBeUnprotected()
    {
        // Use a non-encodable blob — anything that doesn't start with
        // the FakeCredentialProtector marker. The inspector must yield
        // null and the handler must surface a controlled error.
        var (tenantId, applicationId, providerAccountId, _) = await SeedAbacatePayAccount(
            active: true,
            overrideEncryptedCredentials: "not-a-marker:abc");

        // Default inspector+protector won't be able to recover apiKey.
        // We need the same `protector` instance used by the handler — it
        // is `_protector` in the test class — which will throw on
        // `Unprotect`. To match the inspector behaviour (which catches
        // all exceptions and returns null), we expect a controlled
        // InvalidOperationException from the handler.
        var handler = CreateHandler();

        var request = ValidRequest() with { WebhookSecret = "x".PadRight(20, 'y') };
        var act = async () => await handler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*credentials*not in a recognised format*");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- helpers ----

    private ConfigureProviderAccountWebhookHandler CreateHandler()
        => new(_accounts.Object, _protector, _uow.Object, _client, _featurePolicy, _logger);

    private static ConfigureAbacatePayWebhookRequestDto ValidRequest() => new(
        CallbackUrl: "https://merchant.example.com/webhooks/abacate",
        Events: new[] { "transparent.completed", "transparent.refunded" },
        WebhookSecret: null,
        RegisterRemotely: false);

    /// <summary>
    /// Seeds an active AbacatePay <c>ProviderAccount</c> in the
    /// repository, scoped to a fresh tenant/application pair. Returns
    /// the ids so tests can target the right row.
    /// </summary>
    private async Task<(Guid TenantId, Guid ApplicationId, Guid ProviderAccountId, Guid AccountId)>
        SeedAbacatePayAccount(
            bool active,
            string apiKey = "sk_test_seed",
            string? webhookSecret = "seed-webhook-secret",
            string? legacySecret = null,
            string? overrideEncryptedCredentials = null)
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        var account = new ProviderAccount(
            providerAccountId,
            tenantId,
            applicationId,
            ProviderCode.AbacatePay,
            ProviderEnvironment.Sandbox,
            "seed-abacate",
            overrideEncryptedCredentials
                ?? _protector.Protect(BuildCredentialJson(apiKey, webhookSecret, legacySecret)),
            isDefault: false);
        if (!active) account.Disable();

        _accounts.Setup(a => a.GetByIdForTenantAndApplicationAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await Task.CompletedTask;
        return (tenantId, applicationId, providerAccountId, account.Id);
    }

    private async Task<(Guid TenantId, Guid ApplicationId, Guid ProviderAccountId, Guid AccountId)>
        SeedFakeAccount()
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
            "seed-fake",
            _protector.Protect(BuildCredentialJson("k", "s", null)),
            isDefault: false);

        _accounts.Setup(a => a.GetByIdForTenantAndApplicationAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await Task.CompletedTask;
        return (tenantId, applicationId, providerAccountId, account.Id);
    }

    private static string BuildCredentialJson(string apiKey, string? webhookSecret, string? legacySecret)
    {
        if (webhookSecret is null && legacySecret is null)
            return System.Text.Json.JsonSerializer.Serialize(new { apiKey });
        if (webhookSecret is null)
            return System.Text.Json.JsonSerializer.Serialize(new { apiKey, secret = legacySecret });
        return System.Text.Json.JsonSerializer.Serialize(new { apiKey, webhookSecret });
    }
}
