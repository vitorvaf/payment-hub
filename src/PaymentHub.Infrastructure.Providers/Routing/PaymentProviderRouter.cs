using PaymentHub.Application.Abstractions.Providers;

namespace PaymentHub.Infrastructure.Providers.Routing;

public sealed class PaymentProviderRouter : IPaymentProviderRouter
{
    private readonly IReadOnlyDictionary<string, IPaymentProviderAdapter> _adapters;
    private readonly string _defaultProviderCode;

    public PaymentProviderRouter(
        IEnumerable<IPaymentProviderAdapter> adapters,
        string defaultProviderCode)
    {
        _adapters = adapters.ToDictionary(a => a.ProviderCode, StringComparer.OrdinalIgnoreCase);
        _defaultProviderCode = string.IsNullOrWhiteSpace(defaultProviderCode) ? "Fake" : defaultProviderCode;
    }

    public IPaymentProviderAdapter Resolve(string? requestedProviderCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedProviderCode)
            && _adapters.TryGetValue(requestedProviderCode, out var explicitAdapter))
        {
            return explicitAdapter;
        }

        if (!string.IsNullOrWhiteSpace(requestedProviderCode))
        {
            throw new InvalidOperationException($"No provider adapter available for '{requestedProviderCode}'.");
        }

        if (_adapters.TryGetValue(_defaultProviderCode, out var defaultAdapter))
        {
            return defaultAdapter;
        }

        if (_adapters.TryGetValue("Fake", out var fake))
        {
            return fake;
        }

        throw new InvalidOperationException(
            $"No provider adapter available. Requested='{requestedProviderCode}', Default='{_defaultProviderCode}'.");
    }
}
