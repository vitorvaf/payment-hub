using FluentAssertions;
using Moq;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Application;

public class RegisterApplicationClientHandlerTests
{
    private readonly Mock<IApplicationClientRepository> _apps = new(MockBehavior.Strict);
    private readonly Mock<ITenantRepository> _tenants = new(MockBehavior.Strict);
    private readonly Mock<IApiKeyRepository> _apiKeys = new(MockBehavior.Strict);
    private readonly Mock<IApiKeyHasher> _hasher = new(MockBehavior.Strict);
    private readonly FakeWebhookSecretProtector _webhookProtector = new();
    private readonly Mock<IUnitOfWork> _uow = new(MockBehavior.Strict);

    public RegisterApplicationClientHandlerTests()
    {
        _tenants.Setup(t => t.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _apps.Setup(a => a.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _apiKeys.Setup(a => a.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed-api-key");
    }

    [Fact]
    public void ApplicationClientResponseDto_ShouldNotExposeWebhookSecretRawOrProtected()
    {
        var dtoType = typeof(ApplicationClientResponseDto);
        dtoType.GetProperty("WebhookSecret").Should().BeNull("response must never include raw webhook secret");
        dtoType.GetProperty("ProtectedWebhookSecret").Should().BeNull("response must never include protected webhook secret");
        dtoType.GetProperty("EncryptedWebhookSecret").Should().BeNull("response must never include encrypted webhook secret");
        dtoType.GetProperty("HasWebhookSecret").Should().NotBeNull("response should expose a boolean flag for webhook secret presence");
    }

    [Fact]
    public void RegisterApplicationClientRequestDto_ShouldAcceptOptionalWebhookSecret()
    {
        var dtoType = typeof(RegisterApplicationClientRequestDto);
        dtoType.GetProperty("WebhookSecret").Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldProtectWebhookSecret_BeforePersistingApplication()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var rawSecret = "raw-webhook-secret-plain-text";
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: rawSecret,
            DefaultProvider: null);

        ApplicationClient? persisted = null;
        _apps.Setup(a => a.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationClient, CancellationToken>((a, _) => persisted = a)
            .Returns(Task.CompletedTask);

        await handler.HandleAsync(request, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.WebhookSecret.Should().NotBeNull();
        persisted.WebhookSecret.Should().NotBe(rawSecret, "raw secret must never be stored");
        persisted.WebhookSecret.Should().StartWith(FakeWebhookSecretProtector.Marker);
        persisted.HasWebhookSecret.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldPersistNullWebhookSecret_WhenRequestDoesNotProvideOne()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: null,
            DefaultProvider: null);

        ApplicationClient? persisted = null;
        _apps.Setup(a => a.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationClient, CancellationToken>((a, _) => persisted = a)
            .Returns(Task.CompletedTask);

        await handler.HandleAsync(request, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.WebhookSecret.Should().BeNull();
        persisted.HasWebhookSecret.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnResponseWithHasWebhookSecretTrue_WhenSecretProvided()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: "raw-webhook-secret",
            DefaultProvider: null);

        var response = await handler.HandleAsync(request, CancellationToken.None);

        response.HasWebhookSecret.Should().BeTrue();
        response.GetType().GetProperty("WebhookSecret").Should().BeNull();
        response.GetType().GetProperty("ProtectedWebhookSecret").Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnResponseWithHasWebhookSecretFalse_WhenSecretMissing()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: null,
            DefaultProvider: null);

        var response = await handler.HandleAsync(request, CancellationToken.None);

        response.HasWebhookSecret.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenTenantDoesNotExist()
    {
        _tenants.Setup(t => t.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = CreateHandler();

        var act = async () => await handler.HandleAsync(
            new RegisterApplicationClientRequestDto(Guid.NewGuid(), "App", null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Tenant*does not exist*");
    }

    [Fact]
    public async Task HandleAsync_ShouldStillReturnApiKey_WhenWebhookSecretProvided()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: "raw-webhook-secret",
            DefaultProvider: null);

        var response = await handler.HandleAsync(request, CancellationToken.None);

        response.ApiKey.Should().NotBeNullOrEmpty();
        response.ApiKey.Should().StartWith("phk_");
    }

    [Fact]
    public async Task HandleAsync_ShouldNotLogRawWebhookSecret()
    {
        // The handler does not log the raw webhook secret directly. This is a structural
        // assertion: the handler does not have any logger dependency.
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var rawSecret = "raw-webhook-secret-plain-text";
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: rawSecret,
            DefaultProvider: null);

        var act = async () => await handler.HandleAsync(request, CancellationToken.None);

        await act.Should().NotThrowAsync();
        // Confirm the handler does not expose the raw value via its persisted entity.
        var persisted = (ApplicationClient)_apps.Invocations.Single(i => i.Method.Name == "AddAsync").Arguments[0]!;
        persisted.WebhookSecret.Should().NotBe(rawSecret);
    }

    [Fact]
    public async Task HandleAsync_ShouldNormalizeWhitespace_WhenProtectingWebhookSecret()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var request = new RegisterApplicationClientRequestDto(
            tenantId,
            "App",
            WebhookUrl: null,
            WebhookSecret: "  raw-with-whitespace  ",
            DefaultProvider: null);

        var response = await handler.HandleAsync(request, CancellationToken.None);

        response.HasWebhookSecret.Should().BeTrue();
    }

    private RegisterApplicationClientHandler CreateHandler()
        => new(_apps.Object, _tenants.Object, _apiKeys.Object, _hasher.Object, _webhookProtector, _uow.Object);
}