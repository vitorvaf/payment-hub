using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Bootstrap;

namespace PaymentHub.Api.Auth;

public sealed class HostBootstrapPolicy : IBootstrapPolicy
{
    private readonly IHostEnvironment _environment;
    private readonly BootstrapOptions _options;

    public HostBootstrapPolicy(IHostEnvironment environment, IOptions<BootstrapOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public string EnvironmentName => _environment.EnvironmentName ?? string.Empty;

    public bool IsProduction => string.Equals(_environment.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase);

    public bool ShouldAllowProductionBootstrap
        => _options.Enabled && _options.AllowProductionBootstrap && IsProduction;

    public bool ShouldRunDevelopmentSeed
    {
        get
        {
            if (!_options.Enabled) return false;
            if (!_options.SeedDevelopmentData) return false;

            if (IsProduction)
            {
                return _options.AllowProductionBootstrap;
            }

            return string.Equals(_environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_environment.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase);
        }
    }
}
