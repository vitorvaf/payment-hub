using FluentAssertions;
using Moq;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.UnitTests.Application;

public class RegisterProviderAccountHandlerTests
{
    private readonly Mock<IProviderAccountRepository> _accounts = new(MockBehavior.Strict);
    private readonly Mock<ICredentialProtector> _protector = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _uow = new(MockBehavior.Strict);

    public RegisterProviderAccountHandlerTests()
    {
        _accounts.Setup(a => a.AddAsync(It.IsAny<ProviderAccount>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns("protected-blob");
    }

    [Fact]
    public void RegisterProviderAccountRequestDto_ShouldNotExposeTenantIdOrApplicationId()
    {
        var dtoType = typeof(RegisterProviderAccountRequestDto);
        dtoType.GetProperty("TenantId").Should().BeNull("body tenant id is derived from authenticated context");
        dtoType.GetProperty("ApplicationId").Should().BeNull("body application id is derived from authenticated context");
    }

    [Fact]
    public void ProviderAccountResponseDto_ShouldNotExposeApiKeyOrSecret()
    {
        var dtoType = typeof(ProviderAccountResponseDto);
        dtoType.GetProperty("ApiKey").Should().BeNull("response must never include provider credentials");
        dtoType.GetProperty("Secret").Should().BeNull("response must never include provider credentials");
        dtoType.GetProperty("EncryptedCredentials").Should().BeNull("response must never include encrypted credentials blob");
    }

    [Fact]
    public async Task HandleAsync_ShouldUseTenantIdFromCallerAndPersistInCorrectScope()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var request = ValidRequest();

        var result = await handler.HandleAsync(tenantId, applicationId, request, CancellationToken.None);

        result.TenantId.Should().Be(tenantId);
        result.ApplicationId.Should().Be(applicationId);

        ProviderAccount? persisted = null;
        _accounts.Verify(a => a.AddAsync(It.IsAny<ProviderAccount>(), It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Invocations.Single().Arguments[0].Should().BeOfType<ProviderAccount>();
        persisted = (ProviderAccount)_accounts.Invocations.Single().Arguments[0]!;
        persisted.TenantId.Should().Be(tenantId);
        persisted.ApplicationId.Should().Be(applicationId);
        persisted.ProviderCode.Should().Be(ProviderCode.Fake);
        persisted.Environment.Should().Be(ProviderEnvironment.Sandbox);
        persisted.Name.Should().Be("primary");
        persisted.IsDefault.Should().BeTrue();
        persisted.Active.Should().BeTrue();
        persisted.EncryptedCredentials.Should().Be("protected-blob");
    }

    [Fact]
    public async Task HandleAsync_ShouldApplyNameAndEnvironmentFromRequest()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var request = new RegisterProviderAccountRequestDto(
            ProviderCode.Stripe,
            ProviderEnvironment.Production,
            "stripe-prod",
            "sk_live_secret_abcdefghij",
            "whsec_xyz",
            false);

        var result = await handler.HandleAsync(tenantId, applicationId, request, CancellationToken.None);

        result.ProviderCode.Should().Be(ProviderCode.Stripe);
        result.Environment.Should().Be(ProviderEnvironment.Production);
        result.Name.Should().Be("stripe-prod");
        result.IsDefault.Should().BeFalse();

        var persisted = (ProviderAccount)_accounts.Invocations.Single().Arguments[0]!;
        persisted.ProviderCode.Should().Be(ProviderCode.Stripe);
        persisted.Environment.Should().Be(ProviderEnvironment.Production);
        persisted.Name.Should().Be("stripe-prod");
        persisted.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldProtectCredentialsBeforePersisting()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var request = new RegisterProviderAccountRequestDto(
            ProviderCode.Fake,
            ProviderEnvironment.Sandbox,
            "primary",
            "raw-provider-api-key",
            "raw-provider-secret",
            true);

        await handler.HandleAsync(tenantId, applicationId, request, CancellationToken.None);

        _protector.Verify(p => p.Protect(It.IsAny<string>()), Times.Once);
        var serialized = (string)_protector.Invocations.Single().Arguments[0]!;
        serialized.Should().Contain("\"apiKey\":\"raw-provider-api-key\"");
        serialized.Should().Contain("\"secret\":\"raw-provider-secret\"");

        var persisted = (ProviderAccount)_accounts.Invocations.Single().Arguments[0]!;
        persisted.EncryptedCredentials.Should().Be("protected-blob");
    }

    [Fact]
    public async Task HandleAsync_ShouldPersistUsingOnlyCallerTenantAndApplicationIds()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var request = ValidRequest();

        await handler.HandleAsync(tenantId, applicationId, request, CancellationToken.None);

        var persisted = (ProviderAccount)_accounts.Invocations.Single().Arguments[0]!;
        persisted.TenantId.Should().Be(tenantId);
        persisted.ApplicationId.Should().Be(applicationId);
        persisted.TenantId.Should().NotBe(Guid.Empty);
        persisted.ApplicationId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowAndNotPersistWhenTenantIdIsEmpty()
    {
        var handler = CreateHandler();
        var request = ValidRequest();

        var act = async () => await handler.HandleAsync(Guid.Empty, Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tenant*");
        _accounts.Verify(a => a.AddAsync(It.IsAny<ProviderAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowAndNotPersistWhenApplicationIdIsEmpty()
    {
        var handler = CreateHandler();
        var request = ValidRequest();

        var act = async () => await handler.HandleAsync(Guid.NewGuid(), Guid.Empty, request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*application*");
        _accounts.Verify(a => a.AddAsync(It.IsAny<ProviderAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldPersistUniqueIdAndTimestamps()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var result = await handler.HandleAsync(tenantId, applicationId, ValidRequest(), CancellationToken.None);

        result.Id.Should().NotBeEmpty();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var persisted = (ProviderAccount)_accounts.Invocations.Single().Arguments[0]!;
        persisted.Id.Should().Be(result.Id);
        persisted.CreatedAt.Should().Be(result.CreatedAt);
        persisted.UpdatedAt.Should().Be(result.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnResponseWithMatchingScope()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var result = await handler.HandleAsync(tenantId, applicationId, ValidRequest(), CancellationToken.None);

        result.TenantId.Should().Be(tenantId);
        result.ApplicationId.Should().Be(applicationId);
        result.ProviderCode.Should().Be(ProviderCode.Fake);
        result.Environment.Should().Be(ProviderEnvironment.Sandbox);
        result.Name.Should().Be("primary");
        result.IsDefault.Should().BeTrue();
        result.Active.Should().BeTrue();
    }

    private RegisterProviderAccountHandler CreateHandler()
        => new(_accounts.Object, _protector.Object, _uow.Object);

    private static RegisterProviderAccountRequestDto ValidRequest()
        => new(
            ProviderCode.Fake,
            ProviderEnvironment.Sandbox,
            "primary",
            "raw-provider-api-key",
            "raw-provider-secret",
            true);
}
