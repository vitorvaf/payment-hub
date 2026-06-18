using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/provider-accounts")]
public class ProviderAccountsController : ControllerBase
{
    private readonly IRegisterProviderAccountHandler _handler;
    private readonly IValidator<RegisterProviderAccountRequestDto> _validator;
    private readonly ITenantContext _tenantContext;

    public ProviderAccountsController(
        IRegisterProviderAccountHandler handler,
        IValidator<RegisterProviderAccountRequestDto> validator,
        ITenantContext tenantContext)
    {
        _handler = handler;
        _validator = validator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    public async Task<ActionResult<ProviderAccountResponseDto>> Register(
        [FromBody] RegisterProviderAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = "validation_failed", details = validation.Errors });
        }

        Guid tenantId;
        Guid applicationId;
        try
        {
            tenantId = _tenantContext.TenantId;
            applicationId = _tenantContext.ApplicationId;
        }
        catch (InvalidOperationException)
        {
            return Unauthorized(new { error = "unauthorized", message = "Unauthorized" });
        }

        var result = await _handler.HandleAsync(tenantId, applicationId, request, cancellationToken);
        return Created($"/api/v1/provider-accounts/{result.Id}", result);
    }
}
