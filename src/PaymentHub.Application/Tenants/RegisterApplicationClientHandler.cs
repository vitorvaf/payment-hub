using System.Security.Cryptography;
using FluentValidation;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Tenants;

public interface IRegisterApplicationClientHandler
{
    Task<ApplicationClientResponseDto> HandleAsync(RegisterApplicationClientRequestDto request, CancellationToken cancellationToken);
}

public sealed class RegisterApplicationClientHandler : IRegisterApplicationClientHandler
{
    private readonly IApplicationClientRepository _apps;
    private readonly ITenantRepository _tenants;
    private readonly IApiKeyRepository _apiKeys;
    private readonly IApiKeyHasher _hasher;
    private readonly IUnitOfWork _uow;

    public RegisterApplicationClientHandler(
        IApplicationClientRepository apps,
        ITenantRepository tenants,
        IApiKeyRepository apiKeys,
        IApiKeyHasher hasher,
        IUnitOfWork uow)
    {
        _apps = apps;
        _tenants = tenants;
        _apiKeys = apiKeys;
        _hasher = hasher;
        _uow = uow;
    }

    public async Task<ApplicationClientResponseDto> HandleAsync(
        RegisterApplicationClientRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!await _tenants.ExistsAsync(request.TenantId, cancellationToken))
            throw new InvalidOperationException($"Tenant {request.TenantId} does not exist.");

        var app = new ApplicationClient(
            Guid.NewGuid(),
            request.TenantId,
            request.Name,
            request.WebhookUrl);

        if (request.DefaultProvider.HasValue)
            app.SetDefaultProvider(request.DefaultProvider.Value);

        await _apps.AddAsync(app, cancellationToken);

        var rawKey = GenerateRawApiKey();
        var prefix = rawKey[..Math.Min(8, rawKey.Length)];
        var apiKey = new ApiKey(
            Guid.NewGuid(),
            request.TenantId,
            app.Id,
            $"{app.Name}-default",
            _hasher.Hash(rawKey),
            prefix);

        await _apiKeys.AddAsync(apiKey, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ApplicationClientResponseDto(
            app.Id,
            app.TenantId,
            app.Name,
            app.WebhookUrl,
            app.DefaultProvider,
            app.Status.ToString(),
            rawKey);
    }

    private static string GenerateRawApiKey()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return "phk_" + Convert.ToHexString(buffer).ToLowerInvariant();
    }
}

public sealed class RegisterApplicationClientValidator : AbstractValidator<RegisterApplicationClientRequestDto>
{
    public RegisterApplicationClientValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WebhookUrl).MaximumLength(2000).When(x => !string.IsNullOrWhiteSpace(x.WebhookUrl));
    }
}
