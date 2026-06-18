using System.Text.Json;
using FluentValidation;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Tenants;

public interface IRegisterProviderAccountHandler
{
    Task<ProviderAccountResponseDto> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        RegisterProviderAccountRequestDto request,
        CancellationToken cancellationToken);
}

public sealed class RegisterProviderAccountHandler : IRegisterProviderAccountHandler
{
    private readonly IProviderAccountRepository _accounts;
    private readonly ICredentialProtector _protector;
    private readonly IUnitOfWork _uow;

    public RegisterProviderAccountHandler(
        IProviderAccountRepository accounts,
        ICredentialProtector protector,
        IUnitOfWork uow)
    {
        _accounts = accounts;
        _protector = protector;
        _uow = uow;
    }

    public async Task<ProviderAccountResponseDto> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        RegisterProviderAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("Authenticated tenant id is required.");
        if (applicationId == Guid.Empty)
            throw new InvalidOperationException("Authenticated application id is required.");

        var credentials = JsonSerializer.Serialize(new
        {
            apiKey = request.ApiKey,
            secret = request.Secret
        });

        var protectedCredentials = _protector.Protect(credentials);

        var account = new ProviderAccount(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            request.ProviderCode,
            request.Environment,
            request.Name,
            protectedCredentials,
            request.IsDefault);

        await _accounts.AddAsync(account, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ProviderAccountResponseDto(
            account.Id,
            account.TenantId,
            account.ApplicationId,
            account.ProviderCode,
            account.Environment,
            account.Name,
            account.IsDefault,
            account.Active,
            account.CreatedAt);
    }
}

public sealed class RegisterProviderAccountValidator : AbstractValidator<RegisterProviderAccountRequestDto>
{
    public RegisterProviderAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ApiKey).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Secret).MaximumLength(2000).When(x => !string.IsNullOrWhiteSpace(x.Secret));
    }
}
