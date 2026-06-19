using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using PaymentHub.Api.Auth;
using PaymentHub.Application.Abstractions.Bootstrap;

namespace PaymentHub.UnitTests.Api;

public class HostBootstrapPolicyTests
{
    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeFalseInProduction_EvenWithEnabled()
    {
        var policy = CreatePolicy(envName: "Production", enabled: true, seedDev: true, allowProd: false);

        policy.IsProduction.Should().BeTrue();
        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeFalseInProduction_WithoutAllowProductionBootstrap()
    {
        var policy = CreatePolicy(envName: "Production", enabled: true, seedDev: true, allowProd: false);

        policy.ShouldAllowProductionBootstrap.Should().BeFalse();
        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeTrueInProduction_WhenAllowProductionBootstrapIsTrue()
    {
        var policy = CreatePolicy(envName: "Production", enabled: true, seedDev: true, allowProd: true);

        policy.ShouldAllowProductionBootstrap.Should().BeTrue();
        policy.ShouldRunDevelopmentSeed.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeTrueInDevelopment_WhenEnabled()
    {
        var policy = CreatePolicy(envName: "Development", enabled: true, seedDev: true, allowProd: false);

        policy.IsProduction.Should().BeFalse();
        policy.ShouldRunDevelopmentSeed.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeTrueInTest_WhenEnabled()
    {
        var policy = CreatePolicy(envName: "Test", enabled: true, seedDev: true, allowProd: false);

        policy.IsProduction.Should().BeFalse();
        policy.ShouldRunDevelopmentSeed.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeFalseInDevelopment_WhenEnabledButSeedDevelopmentDataFalse()
    {
        var policy = CreatePolicy(envName: "Development", enabled: true, seedDev: false, allowProd: false);

        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeFalseInDevelopment_WhenBootstrapDisabled()
    {
        var policy = CreatePolicy(envName: "Development", enabled: false, seedDev: true, allowProd: false);

        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeFalseInStaging_WhenNotEnabled()
    {
        var policy = CreatePolicy(envName: "Staging", enabled: false, seedDev: true, allowProd: false);

        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeTrueInStaging_WhenEnabled()
    {
        var policy = CreatePolicy(envName: "Staging", enabled: true, seedDev: true, allowProd: false);

        policy.ShouldRunDevelopmentSeed.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunDevelopmentSeed_ShouldBeFalseInUnknownEnvironment_WhenEnabled()
    {
        var policy = CreatePolicy(envName: "QA", enabled: true, seedDev: true, allowProd: false);

        policy.IsProduction.Should().BeFalse();
        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
    }

    [Fact]
    public void EnvironmentName_ShouldReturnConfiguredEnvironmentName()
    {
        var policy = CreatePolicy(envName: "Development", enabled: false, seedDev: false, allowProd: false);

        policy.EnvironmentName.Should().Be("Development");
    }

    [Fact]
    public void MissingConfiguration_ShouldProduceSafePolicy()
    {
        var policy = new HostBootstrapPolicy(
            new TestHostEnvironment { EnvironmentName = "Production" },
            Microsoft.Extensions.Options.Options.Create(new BootstrapOptions()));

        policy.IsProduction.Should().BeTrue();
        policy.ShouldRunDevelopmentSeed.Should().BeFalse();
        policy.ShouldAllowProductionBootstrap.Should().BeFalse();
    }

    private static HostBootstrapPolicy CreatePolicy(string envName, bool enabled, bool seedDev, bool allowProd)
    {
        var options = new BootstrapOptions
        {
            Enabled = enabled,
            SeedDevelopmentData = seedDev,
            AllowProductionBootstrap = allowProd
        };

        return new HostBootstrapPolicy(
            new TestHostEnvironment { EnvironmentName = envName },
            Microsoft.Extensions.Options.Options.Create(options));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PaymentHub.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
