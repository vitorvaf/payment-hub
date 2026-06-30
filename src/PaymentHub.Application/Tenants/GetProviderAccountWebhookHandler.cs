using System.Text.Json;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Tenants;

/// <summary>
/// Tagged outcome of <see cref="IGetProviderAccountWebhookHandler"/>.
/// Mirrors the boundary used by the configure handler so the controller
/// stays focused on status-code mapping.
/// </summary>
public abstract record GetWebhookOutcome
{
    private GetWebhookOutcome() { }

    public sealed record Success(ProviderAccountWebhookResponseDto Response) : GetWebhookOutcome;

    /// <summary>
    /// The provider account id was not found in the authenticated
    /// tenant/application scope. Maps to <c>404 Not Found</c>.
    /// </summary>
    public sealed record NotFound : GetWebhookOutcome;

    /// <summary>
    /// The provider account exists in scope but is marked inactive.
    /// Maps to <c>409 Conflict</c>.
    /// </summary>
    public sealed record Inactive : GetWebhookOutcome;

    /// <summary>
    /// The provider code of the account is not AbacatePay.
    /// Maps to <c>409 Conflict</c>.
    /// </summary>
    public sealed record UnsupportedProvider : GetWebhookOutcome;
}

public interface IGetProviderAccountWebhookHandler
{
    Task<GetWebhookOutcome> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        Guid providerAccountId,
        CancellationToken cancellationToken);
}

public sealed class GetProviderAccountWebhookHandler
    : IGetProviderAccountWebhookHandler
{
    private readonly IProviderAccountRepository _accounts;
    private readonly ICredentialProtector _protector;

    public GetProviderAccountWebhookHandler(
        IProviderAccountRepository accounts,
        ICredentialProtector protector)
    {
        _accounts = accounts;
        _protector = protector;
    }

    public async Task<GetWebhookOutcome> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        Guid providerAccountId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("Authenticated tenant id is required.");
        if (applicationId == Guid.Empty)
            throw new InvalidOperationException("Authenticated application id is required.");
        if (providerAccountId == Guid.Empty)
            throw new InvalidOperationException("ProviderAccount id is required.");

        var account = await _accounts.GetByIdForTenantAndApplicationAsync(
            tenantId, applicationId, providerAccountId, cancellationToken);
        if (account is null) return new GetWebhookOutcome.NotFound();

        if (!account.Active) return new GetWebhookOutcome.Inactive();

        if (account.ProviderCode != ProviderCode.AbacatePay)
            return new GetWebhookOutcome.UnsupportedProvider();

        var hasSecret = ProviderAccountCredentialsInspector.HasWebhookSecret(
            _protector,
            account.EncryptedCredentials);

        return new GetWebhookOutcome.Success(new ProviderAccountWebhookResponseDto(
            ProviderAccountId: account.Id,
            ProviderCode: account.ProviderCode,
            Environment: account.Environment,
            CallbackUrl: account.WebhookCallbackUrl,
            Events: ParseEventsOrEmpty(account.WebhookEvents),
            HasWebhookSecret: hasSecret,
            RemoteRegistrationStatus: account.WebhookRemoteStatus?.ToString(),
            ConfiguredAt: account.WebhookConfiguredAt,
            UpdatedAt: account.UpdatedAt));
    }

    private static IReadOnlyList<string> ParseEventsOrEmpty(string? eventsJson)
    {
        if (string.IsNullOrWhiteSpace(eventsJson)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(eventsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var output = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var s = element.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) output.Add(s);
                }
            }
            return output.Count == 0 ? Array.Empty<string>() : output;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
