using PaymentHub.Domain.Enums;
using PaymentHub.Domain.ValueObjects;

namespace PaymentHub.Domain.Entities;

public class Payment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string ExternalReference { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = Money.Zero("BRL");
    public string Currency { get; private set; } = "BRL";
    public ProviderCode SelectedProvider { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Created;
    public string? ProviderPaymentId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? CustomerEmail { get; private set; }
    public string? CustomerName { get; private set; }
    public string? SuccessUrl { get; private set; }
    public string? CancelUrl { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    private readonly List<PaymentAttempt> _attempts = new();
    public IReadOnlyCollection<PaymentAttempt> Attempts => _attempts.AsReadOnly();

    private Payment() { }

    public Payment(
        Guid id,
        Guid tenantId,
        Guid applicationId,
        string externalReference,
        Money amount,
        ProviderCode selectedProvider,
        string? customerEmail,
        string? customerName,
        string? successUrl,
        string? cancelUrl,
        string? metadataJson = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (applicationId == Guid.Empty) throw new ArgumentException("ApplicationId is required.", nameof(applicationId));
        if (string.IsNullOrWhiteSpace(externalReference))
            throw new ArgumentException("ExternalReference is required.", nameof(externalReference));
        if (amount is null) throw new ArgumentNullException(nameof(amount));
        if (amount.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));

        Id = id;
        TenantId = tenantId;
        ApplicationId = applicationId;
        ExternalReference = externalReference.Trim();
        Amount = amount;
        Currency = amount.Currency;
        SelectedProvider = selectedProvider;
        CustomerEmail = customerEmail;
        CustomerName = customerName;
        SuccessUrl = successUrl;
        CancelUrl = cancelUrl;
        MetadataJson = metadataJson;
        Status = PaymentStatus.Created;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void AttachProviderResult(string? providerPaymentId, string? checkoutUrl, PaymentStatus newStatus)
    {
        ProviderPaymentId = providerPaymentId;
        CheckoutUrl = checkoutUrl;
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkPending()
    {
        Status = PaymentStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ApplyProviderStatus(PaymentStatus newStatus, string? providerPaymentId = null)
    {
        if (providerPaymentId is not null) ProviderPaymentId = providerPaymentId;
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
        if (newStatus is PaymentStatus.Approved or PaymentStatus.Rejected or PaymentStatus.Failed
            or PaymentStatus.Refunded or PaymentStatus.Chargeback or PaymentStatus.Cancelled or PaymentStatus.Expired)
        {
            ProcessedAt = DateTime.UtcNow;
        }
    }

    public PaymentAttempt RegisterAttempt(PaymentAttemptStatus status, string? providerPaymentId, string? errorMessage = null)
    {
        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            Id,
            TenantId,
            ApplicationId,
            SelectedProvider,
            status,
            providerPaymentId,
            errorMessage);
        _attempts.Add(attempt);
        UpdatedAt = DateTime.UtcNow;
        return attempt;
    }
}
