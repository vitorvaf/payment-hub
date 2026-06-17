namespace PaymentHub.Application.Abstractions.Context;

public interface IRuntimeEnvironment
{
    bool IsDevelopment { get; }
}
