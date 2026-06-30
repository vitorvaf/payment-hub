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
    private readonly IValidator<ConfigureAbacatePayWebhookRequestDto> _webhookValidator;
    private readonly IConfigureProviderAccountWebhookHandler _configureWebhookHandler;
    private readonly IGetProviderAccountWebhookHandler _getWebhookHandler;

    public ProviderAccountsController(
        IRegisterProviderAccountHandler handler,
        IValidator<RegisterProviderAccountRequestDto> validator,
        ITenantContext tenantContext,
        IValidator<ConfigureAbacatePayWebhookRequestDto> webhookValidator,
        IConfigureProviderAccountWebhookHandler configureWebhookHandler,
        IGetProviderAccountWebhookHandler getWebhookHandler)
    {
        _handler = handler;
        _validator = validator;
        _tenantContext = tenantContext;
        _webhookValidator = webhookValidator;
        _configureWebhookHandler = configureWebhookHandler;
        _getWebhookHandler = getWebhookHandler;
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

    /// <summary>
    /// Slice 2-C. Configures the AbacatePay webhook subscription for an
    /// existing provider account. The response is the same shape as GET.
    /// </summary>
    [HttpPut("{providerAccountId:guid}/webhook")]
    public async Task<ActionResult<ProviderAccountWebhookResponseDto>> ConfigureWebhook(
        Guid providerAccountId,
        [FromBody] ConfigureAbacatePayWebhookRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { error = "validation_failed", details = new[] { new { ErrorMessage = "Request body is required." } } });

        var validation = await _webhookValidator.ValidateAsync(request, cancellationToken);
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

        var outcome = await _configureWebhookHandler.HandleAsync(
            tenantId, applicationId, providerAccountId, request, cancellationToken);

        return outcome switch
        {
            ConfigureWebhookOutcome.Success success => Ok(success.Response),
            ConfigureWebhookOutcome.NotFound => NotFound(new { error = "provider_account_not_found" }),
            ConfigureWebhookOutcome.Inactive => Conflict(new { error = "provider_account_inactive" }),
            ConfigureWebhookOutcome.UnsupportedProvider => Conflict(new { error = "unsupported_provider" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    /// <summary>
    /// Slice 2-C. Returns the non-sensitive AbacatePay webhook
    /// configuration for the given provider account. Never includes
    /// <c>apiKey</c>, raw <c>webhookSecret</c>, the protected blob or
    /// anything else sensitive.
    /// </summary>
    [HttpGet("{providerAccountId:guid}/webhook")]
    public async Task<ActionResult<ProviderAccountWebhookResponseDto>> GetWebhook(
        Guid providerAccountId,
        CancellationToken cancellationToken)
    {
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

        var outcome = await _getWebhookHandler.HandleAsync(
            tenantId, applicationId, providerAccountId, cancellationToken);

        return outcome switch
        {
            GetWebhookOutcome.Success success => Ok(success.Response),
            GetWebhookOutcome.NotFound => NotFound(new { error = "provider_account_not_found" }),
            GetWebhookOutcome.Inactive => Conflict(new { error = "provider_account_inactive" }),
            GetWebhookOutcome.UnsupportedProvider => Conflict(new { error = "unsupported_provider" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
