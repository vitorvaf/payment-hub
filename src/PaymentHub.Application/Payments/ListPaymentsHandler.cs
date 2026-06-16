using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Payments.Dtos;

namespace PaymentHub.Application.Payments;

public interface IListPaymentsHandler
{
    Task<IReadOnlyList<PaymentListItemDto>> HandleAsync(
        Guid tenantId, Guid applicationId, int skip, int take, CancellationToken cancellationToken);
}

public sealed class ListPaymentsHandler : IListPaymentsHandler
{
    private readonly IPaymentRepository _payments;

    public ListPaymentsHandler(IPaymentRepository payments)
    {
        _payments = payments;
    }

    public async Task<IReadOnlyList<PaymentListItemDto>> HandleAsync(
        Guid tenantId, Guid applicationId, int skip, int take, CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 200);

        var payments = await _payments.ListAsync(tenantId, applicationId, skip, take, cancellationToken);
        return payments.Select(p => new PaymentListItemDto(
            p.Id,
            p.ExternalReference,
            p.Amount.Amount,
            p.Currency,
            p.SelectedProvider,
            p.Status,
            p.CreatedAt)).ToList();
    }
}
