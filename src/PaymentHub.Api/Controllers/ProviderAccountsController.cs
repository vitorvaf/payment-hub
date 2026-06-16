using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/provider-accounts")]
public class ProviderAccountsController : ControllerBase
{
    private readonly IRegisterProviderAccountHandler _handler;
    private readonly IValidator<RegisterProviderAccountRequestDto> _validator;

    public ProviderAccountsController(
        IRegisterProviderAccountHandler handler,
        IValidator<RegisterProviderAccountRequestDto> validator)
    {
        _handler = handler;
        _validator = validator;
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

        var result = await _handler.HandleAsync(request, cancellationToken);
        return Created($"/api/v1/provider-accounts/{result.Id}", result);
    }
}
