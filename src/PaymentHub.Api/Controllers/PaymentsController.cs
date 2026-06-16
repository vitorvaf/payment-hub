using Microsoft.AspNetCore.Mvc;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Payments;
using PaymentHub.Application.Payments.Dtos;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IGetPaymentByIdHandler _getById;
    private readonly IListPaymentsHandler _list;
    private readonly ITenantContext _tenantContext;

    public PaymentsController(
        IGetPaymentByIdHandler getById,
        IListPaymentsHandler list,
        ITenantContext tenantContext)
    {
        _getById = getById;
        _list = list;
        _tenantContext = tenantContext;
    }

    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PaymentResponseDto>> GetById(Guid paymentId, CancellationToken cancellationToken)
    {
        var result = await _getById.HandleAsync(_tenantContext.TenantId, paymentId, cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentListItemDto>>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _list.HandleAsync(_tenantContext.TenantId, _tenantContext.ApplicationId, skip, take, cancellationToken);
        return Ok(result);
    }
}
