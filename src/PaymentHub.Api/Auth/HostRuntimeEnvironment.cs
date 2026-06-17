using Microsoft.Extensions.Hosting;
using PaymentHub.Application.Abstractions.Context;

namespace PaymentHub.Api.Auth;

public sealed class HostRuntimeEnvironment : IRuntimeEnvironment
{
    private readonly IHostEnvironment _environment;

    public HostRuntimeEnvironment(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public bool IsDevelopment => _environment.IsDevelopment();
}
