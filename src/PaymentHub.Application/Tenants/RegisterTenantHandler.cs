using FluentValidation;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Tenants;

public interface IRegisterTenantHandler
{
    Task<TenantResponseDto> HandleAsync(RegisterTenantRequestDto request, CancellationToken cancellationToken);
}

public sealed class RegisterTenantHandler : IRegisterTenantHandler
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;

    public RegisterTenantHandler(ITenantRepository tenants, IUnitOfWork uow)
    {
        _tenants = tenants;
        _uow = uow;
    }

    public async Task<TenantResponseDto> HandleAsync(RegisterTenantRequestDto request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant(Guid.NewGuid(), request.Name, request.Slug);
        await _tenants.AddAsync(tenant, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new TenantResponseDto(tenant.Id, tenant.Name, tenant.Slug, tenant.Status.ToString(), tenant.CreatedAt);
    }
}

public sealed class RegisterTenantValidator : AbstractValidator<RegisterTenantRequestDto>
{
    public RegisterTenantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(80)
            .Matches("^[a-z0-9-]+$").WithMessage("Slug must contain only lowercase letters, numbers and dashes.");
    }
}
