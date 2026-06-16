using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Payments.Dtos;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Payments;

public interface IGetPaymentByIdHandler
{
    Task<PaymentResponseDto?> HandleAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken);
}

public sealed class GetPaymentByIdHandler : IGetPaymentByIdHandler
{
    private readonly IPaymentRepository _payments;

    public GetPaymentByIdHandler(IPaymentRepository payments)
    {
        _payments = payments;
    }

    public async Task<PaymentResponseDto?> HandleAsync(Guid tenantId, Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdForTenantAsync(tenantId, paymentId, cancellationToken);
        if (payment is null) return null;
        return MapToDto(payment);
    }

    internal static PaymentResponseDto MapToDto(Payment payment) => new(
        payment.Id,
        payment.TenantId,
        payment.ApplicationId,
        payment.ExternalReference,
        payment.Amount.Amount,
        payment.Currency,
        payment.SelectedProvider,
        payment.Status,
        payment.ProviderPaymentId,
        payment.CheckoutUrl,
        payment.CustomerEmail,
        payment.CustomerName,
        payment.CreatedAt,
        payment.ProcessedAt);
}
