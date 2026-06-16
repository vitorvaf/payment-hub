using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
public class TenantsController : ControllerBase
{
    private readonly IRegisterTenantHandler _handler;
    private readonly IValidator<RegisterTenantRequestDto> _validator;

    public TenantsController(IRegisterTenantHandler handler, IValidator<RegisterTenantRequestDto> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    [HttpPost]
    public async Task<ActionResult<TenantResponseDto>> Register(
        [FromBody] RegisterTenantRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = "validation_failed", details = validation.Errors });
        }

        var result = await _handler.HandleAsync(request, cancellationToken);
        return Created($"/api/v1/tenants/{result.Id}", result);
    }
}
