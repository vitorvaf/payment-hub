using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentHub.Application.Abstractions.Bootstrap;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Bootstrap;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.UnitTests.Application;

public class DevelopmentDataSeederTests
{
    private readonly Mock<IBootstrapPolicy> _policy = new();
    private readonly Mock<ITenantRepository> _tenants = new(MockBehavior.Strict);
    private readonly Mock<IApplicationClientRepository> _applications = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _uow = new(MockBehavior.Strict);

    public DevelopmentDataSeederTests()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task SeedAsync_ShouldSkip_WhenPolicyDisallowsInProduction()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(false);
        _policy.SetupGet(p => p.IsProduction).Returns(true);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Production");

        var seeder = CreateSeeder();
        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.SeedExecuted.Should().BeFalse();
        outcome.TenantCreated.Should().BeFalse();
        outcome.ApplicationCreated.Should().BeFalse();
        outcome.EnvironmentName.Should().Be("Production");
        outcome.Reason.Should().Contain("Production");
        _tenants.VerifyNoOtherCalls();
        _applications.VerifyNoOtherCalls();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_ShouldSkip_WhenBootstrapIsDisabled()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(false);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        var seeder = CreateSeeder();
        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.SeedExecuted.Should().BeFalse();
        outcome.TenantCreated.Should().BeFalse();
        outcome.ApplicationCreated.Should().BeFalse();
        outcome.Reason.Should().Contain("Bootstrap:Enabled");
        _tenants.VerifyNoOtherCalls();
        _applications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateTenantAndApplication_WhenAllowedAndDatabaseIsEmpty()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        _tenants.Setup(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        _tenants.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _applications.Setup(r => r.GetByTenantAndNameAsync(It.IsAny<Guid>(), "dev-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationClient?)null);
        _applications.Setup(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var seeder = CreateSeeder();
        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.SeedExecuted.Should().BeTrue();
        outcome.TenantCreated.Should().BeTrue();
        outcome.ApplicationCreated.Should().BeTrue();
        outcome.EnvironmentName.Should().Be("Development");
        _tenants.Verify(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()), Times.Once);
        _tenants.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Once);
        _applications.Verify(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent_ReusingExistingTenantAndApplication()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        var existingTenant = new Tenant(Guid.NewGuid(), "Development Tenant (dev-tenant)", "dev-tenant");
        var existingApplication = new ApplicationClient(Guid.NewGuid(), existingTenant.Id, "dev-app");
        _tenants.Setup(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTenant);
        _applications.Setup(r => r.GetByTenantAndNameAsync(existingTenant.Id, "dev-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApplication);

        var seeder = CreateSeeder();
        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.SeedExecuted.Should().BeFalse();
        outcome.TenantCreated.Should().BeFalse();
        outcome.ApplicationCreated.Should().BeFalse();
        outcome.Reason.Should().Contain("existing");
        _tenants.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
        _applications.Verify(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateApplicationOnly_WhenTenantAlreadyExists()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        var existingTenant = new Tenant(Guid.NewGuid(), "Development Tenant (dev-tenant)", "dev-tenant");
        _tenants.Setup(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTenant);
        _applications.Setup(r => r.GetByTenantAndNameAsync(existingTenant.Id, "dev-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationClient?)null);
        _applications.Setup(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var seeder = CreateSeeder();
        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.TenantCreated.Should().BeFalse();
        outcome.ApplicationCreated.Should().BeTrue();
        outcome.SeedExecuted.Should().BeTrue();
        _tenants.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
        _applications.Verify(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_ShouldPersistTenantAndApplicationWithActiveStatus()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        Tenant? persistedTenant = null;
        ApplicationClient? persistedApp = null;
        _tenants.Setup(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        _tenants.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Callback<Tenant, CancellationToken>((t, _) => persistedTenant = t)
            .Returns(Task.CompletedTask);
        _applications.Setup(r => r.GetByTenantAndNameAsync(It.IsAny<Guid>(), "dev-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationClient?)null);
        _applications.Setup(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationClient, CancellationToken>((a, _) => persistedApp = a)
            .Returns(Task.CompletedTask);

        var seeder = CreateSeeder();
        await seeder.SeedAsync(CancellationToken.None);

        persistedTenant.Should().NotBeNull();
        persistedTenant!.Status.Should().Be(TenantStatus.Active);
        persistedTenant.Slug.Should().Be("dev-tenant");
        persistedApp.Should().NotBeNull();
        persistedApp!.Status.Should().Be(ApplicationStatus.Active);
        persistedApp.TenantId.Should().Be(persistedTenant.Id);
        persistedApp.Name.Should().Be("dev-app");
    }

    [Fact]
    public async Task SeedAsync_ShouldNotLogApiKeyOrSecrets()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        _tenants.Setup(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        _tenants.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _applications.Setup(r => r.GetByTenantAndNameAsync(It.IsAny<Guid>(), "dev-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationClient?)null);
        _applications.Setup(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new RecordingLogger<DevelopmentDataSeeder>();
        var seeder = new DevelopmentDataSeeder(
            _policy.Object,
            Microsoft.Extensions.Options.Options.Create(new BootstrapOptions
            {
                Enabled = true,
                SeedDevelopmentData = true
            }),
            _tenants.Object,
            _applications.Object,
            _uow.Object,
            logger);

        await seeder.SeedAsync(CancellationToken.None);

        logger.Messages.Should().NotBeEmpty();
        foreach (var message in logger.Messages)
        {
            message.Should().NotContainEquivalentOf("apiKey=");
            message.Should().NotContainEquivalentOf("secret=");
            message.Should().NotContainEquivalentOf("password=");
            message.Should().NotContainEquivalentOf("phk_");
            message.Should().NotContainEquivalentOf("Bearer ");
        }
    }

    [Fact]
    public async Task SeedAsync_ShouldFailSafely_WhenBootstrapOptionsMissingRequiredNames()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(false);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Development");

        var seeder = new DevelopmentDataSeeder(
            _policy.Object,
            Microsoft.Extensions.Options.Options.Create(new BootstrapOptions
            {
                Enabled = true,
                SeedDevelopmentData = true,
                DevelopmentTenantSlug = null,
                DevelopmentApplicationName = null
            }),
            _tenants.Object,
            _applications.Object,
            _uow.Object,
            NullLogger<DevelopmentDataSeeder>.Instance);

        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.SeedExecuted.Should().BeFalse();
        outcome.TenantCreated.Should().BeFalse();
        outcome.ApplicationCreated.Should().BeFalse();
        outcome.Reason.Should().Contain("missing");
        _tenants.VerifyNoOtherCalls();
        _applications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SeedAsync_ShouldOnlyRunInProduction_WhenAllowProductionBootstrapIsOptIn()
    {
        _policy.SetupGet(p => p.ShouldRunDevelopmentSeed).Returns(true);
        _policy.SetupGet(p => p.IsProduction).Returns(true);
        _policy.SetupGet(p => p.EnvironmentName).Returns("Production");

        _tenants.Setup(r => r.GetBySlugAsync("dev-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        _tenants.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _applications.Setup(r => r.GetByTenantAndNameAsync(It.IsAny<Guid>(), "dev-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationClient?)null);
        _applications.Setup(r => r.AddAsync(It.IsAny<ApplicationClient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var seeder = CreateSeeder();
        var outcome = await seeder.SeedAsync(CancellationToken.None);

        outcome.SeedExecuted.Should().BeTrue();
        outcome.TenantCreated.Should().BeTrue();
        outcome.EnvironmentName.Should().Be("Production");
    }

    private DevelopmentDataSeeder CreateSeeder()
        => new(
            _policy.Object,
            Microsoft.Extensions.Options.Options.Create(new BootstrapOptions
            {
                Enabled = true,
                SeedDevelopmentData = true,
                DevelopmentTenantSlug = "dev-tenant",
                DevelopmentApplicationName = "dev-app"
            }),
            _tenants.Object,
            _applications.Object,
            _uow.Object,
            NullLogger<DevelopmentDataSeeder>.Instance);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopDisposable();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
