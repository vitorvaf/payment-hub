namespace PaymentHub.Application.Abstractions.Bootstrap;

public interface IBootstrapPolicy
{
    string EnvironmentName { get; }

    bool IsProduction { get; }

    bool ShouldRunDevelopmentSeed { get; }

    bool ShouldAllowProductionBootstrap { get; }
}
