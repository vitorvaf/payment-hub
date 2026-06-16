using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/applications")]
public class ApplicationsController : ControllerBase
{
    private readonly IRegisterApplicationClientHandler _handler;
    private readonly IValidator<RegisterApplicationClientRequestDto> _validator;

    public ApplicationsController(
        IRegisterApplicationClientHandler handler,
        IValidator<RegisterApplicationClientRequestDto> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationClientResponseDto>> Register(
        [FromBody] RegisterApplicationClientRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = "validation_failed", details = validation.Errors });
        }

        var result = await _handler.HandleAsync(request, cancellationToken);
        return Created($"/api/v1/applications/{result.Id}", result);
    }
}
