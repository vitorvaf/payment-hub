using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants;

namespace PaymentHub.Infrastructure.Postgres.Security;

/// <summary>
/// Default <see cref="IProviderAccountCredentialsReader"/>: a thin
/// adapter over the existing <see cref="ProviderAccountCredentialsInspector"/>.
/// Lives in <c>Infrastructure.Postgres</c> so it can be
/// composed with the real <see cref="ICredentialProtector"/>; the
/// application-layer inspector remains the source of truth for the
/// zero-allocation + no-exception guarantee.
/// </summary>
public sealed class ProviderAccountCredentialsReader : IProviderAccountCredentialsReader
{
    private readonly ICredentialProtector _protector;

    public ProviderAccountCredentialsReader(ICredentialProtector protector)
    {
        _protector = protector;
    }

    public string? ReadApiKey(string encryptedCredentials)
        => ProviderAccountCredentialsInspector.UnprotectAndReadApiKey(_protector, encryptedCredentials);
}
