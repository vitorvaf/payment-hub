using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Checkouts;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/checkouts")]
public class CheckoutsController : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";

    private readonly ICreateCheckoutHandler _handler;
    private readonly IValidator<CreateCheckoutRequestDto> _validator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CheckoutsController> _logger;

    public CheckoutsController(
        ICreateCheckoutHandler handler,
        IValidator<CreateCheckoutRequestDto> validator,
        ITenantContext tenantContext,
        ILogger<CheckoutsController> logger)
    {
        _handler = handler;
        _validator = validator;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CreateCheckoutResponse>> Create(
        [FromBody] CreateCheckoutRequestDto request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        [FromHeader(Name = "X-Provider")] string? providerCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { error = "missing_idempotency_key", message = "Idempotency-Key header is required." });
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = "validation_failed", details = validation.Errors });
        }

        try
        {
            var tenantId = _tenantContext.TenantId;
            var applicationId = _tenantContext.ApplicationId;
            var result = await _handler.HandleAsync(
                tenantId, applicationId, idempotencyKey, request, providerCode, cancellationToken);
            return Created($"/api/v1/payments/{result.PaymentId}", result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Checkout creation failed");
            return UnprocessableEntity(new { error = "checkout_failed", message = ex.Message });
        }
    }
}
