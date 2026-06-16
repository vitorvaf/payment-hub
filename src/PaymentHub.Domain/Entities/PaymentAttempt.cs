using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Entities;

public class PaymentAttempt
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public ProviderCode ProviderCode { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private PaymentAttempt() { }

    public PaymentAttempt(
        Guid id,
        Guid paymentId,
        Guid tenantId,
        Guid applicationId,
        ProviderCode providerCode,
        PaymentAttemptStatus status,
        string? providerPaymentId,
        string? errorMessage)
    {
        Id = id;
        PaymentId = paymentId;
        TenantId = tenantId;
        ApplicationId = applicationId;
        ProviderCode = providerCode;
        Status = status;
        ProviderPaymentId = providerPaymentId;
        ErrorMessage = errorMessage;
        CreatedAt = DateTime.UtcNow;
    }
}
