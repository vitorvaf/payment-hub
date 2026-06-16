using System.Text.Json;
using FluentValidation;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Checkouts;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.ValueObjects;

namespace PaymentHub.Application.Checkouts;

public interface ICreateCheckoutHandler
{
    Task<CreateCheckoutResponse> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        string idempotencyKey,
        CreateCheckoutRequestDto request,
        string? requestedProviderCode,
        CancellationToken cancellationToken);
}

public sealed class CreateCheckoutHandler : ICreateCheckoutHandler
{
    private readonly ITenantRepository _tenants;
    private readonly IApplicationClientRepository _apps;
    private readonly IProviderAccountRepository _accounts;
    private readonly IPaymentRepository _payments;
    private readonly IIdempotencyKeyRepository _idempotency;
    private readonly IIdempotencyRequestHasher _requestHasher;
    private readonly IPaymentProviderRouter _router;
    private readonly IOutboxPublisher _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateCheckoutHandler(
        ITenantRepository tenants,
        IApplicationClientRepository apps,
        IProviderAccountRepository accounts,
        IPaymentRepository payments,
        IIdempotencyKeyRepository idempotency,
        IIdempotencyRequestHasher requestHasher,
        IPaymentProviderRouter router,
        IOutboxPublisher outbox,
        IUnitOfWork uow,
        IClock clock)
    {
        _tenants = tenants;
        _apps = apps;
        _accounts = accounts;
        _payments = payments;
        _idempotency = idempotency;
        _requestHasher = requestHasher;
        _router = router;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<CreateCheckoutResponse> HandleAsync(
        Guid tenantId,
        Guid applicationId,
        string idempotencyKey,
        CreateCheckoutRequestDto request,
        string? requestedProviderCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidOperationException("Idempotency-Key is required.");

        if (!await _tenants.ExistsAsync(tenantId, cancellationToken))
            throw new InvalidOperationException("Tenant not found.");

        var application = await _apps.GetByTenantAndIdAsync(tenantId, applicationId, cancellationToken)
            ?? throw new InvalidOperationException("Application not found for tenant.");

        var existing = await _idempotency.FindAsync(tenantId, applicationId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var existingPayment = await _payments.GetByIdAsync(existing.PaymentId, cancellationToken);
            if (existingPayment is not null)
            {
                return new CreateCheckoutResponse(
                    existingPayment.Id,
                    existingPayment.Status.ToString(),
                    existingPayment.SelectedProvider.ToString(),
                    existingPayment.CheckoutUrl);
            }
        }

        var amount = ComputeAmountInCents(request.Items);
        var money = Money.Of(amount, string.IsNullOrWhiteSpace(request.Currency) ? "BRL" : request.Currency);
        var metadataJson = request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata);

        var providerCode = await ResolveProviderAsync(
            tenantId, applicationId, application.DefaultProvider, requestedProviderCode, cancellationToken);

        var adapter = _router.Resolve(providerCode.ToString());

        var payment = new Payment(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            request.ExternalReference,
            money,
            providerCode,
            request.Customer?.Email,
            request.Customer?.Name,
            request.SuccessUrl,
            request.CancelUrl,
            metadataJson);

        await _payments.AddAsync(payment, cancellationToken);

        var providerRequest = new CreateCheckoutProviderRequest(
            tenantId,
            applicationId,
            payment.Id,
            payment.ExternalReference,
            money.Amount,
            money.Currency,
            payment.CustomerEmail,
            payment.CustomerName,
            payment.SuccessUrl,
            payment.CancelUrl,
            payment.MetadataJson,
            request.Items.Select(i => new ProviderCheckoutItem(i.Id, i.Name, i.Quantity, i.UnitAmount)).ToList());

        var providerResult = await adapter.CreateCheckoutAsync(providerRequest, cancellationToken);

        if (!providerResult.Success)
        {
            payment.RegisterAttempt(
                PaymentAttemptStatus.Failed,
                providerResult.ProviderPaymentId,
                providerResult.ErrorMessage ?? "Provider error");
            await _uow.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(providerResult.ErrorMessage ?? "Provider failed to create checkout.");
        }

        payment.AttachProviderResult(
            providerResult.ProviderPaymentId,
            providerResult.CheckoutUrl,
            PaymentStatus.Pending);

        payment.RegisterAttempt(
            PaymentAttemptStatus.Succeeded,
            providerResult.ProviderPaymentId,
            null);

        var idemKey = new IdempotencyKey(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            idempotencyKey,
            _requestHasher.Hash(JsonSerializer.Serialize(request)),
            payment.Id);
        await _idempotency.AddAsync(idemKey, cancellationToken);

        await _outbox.EnqueueAsync(
            tenantId,
            applicationId,
            "payment.checkout.created",
            new
            {
                paymentId = payment.Id,
                externalReference = payment.ExternalReference,
                amount = money.Amount,
                currency = money.Currency,
                provider = providerCode.ToString(),
                status = payment.Status.ToString(),
                checkoutUrl = payment.CheckoutUrl,
                providerPaymentId = payment.ProviderPaymentId,
                createdAt = payment.CreatedAt
            },
            cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new CreateCheckoutResponse(
            payment.Id,
            payment.Status.ToString(),
            payment.SelectedProvider.ToString(),
            payment.CheckoutUrl);
    }

    private static long ComputeAmountInCents(IReadOnlyList<CheckoutItemDto> items)
    {
        if (items is null || items.Count == 0)
            throw new InvalidOperationException("At least one item is required.");
        long total = 0;
        foreach (var item in items)
        {
            if (item.Quantity <= 0) throw new InvalidOperationException("Item quantity must be positive.");
            if (item.UnitAmount <= 0) throw new InvalidOperationException("Item unit amount must be positive.");
            total += item.UnitAmount * item.Quantity;
        }
        return total;
    }

    private async Task<ProviderCode> ResolveProviderAsync(
        Guid tenantId,
        Guid applicationId,
        ProviderCode? applicationDefault,
        string? requested,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            Enum.TryParse<ProviderCode>(requested, ignoreCase: true, out var explicitCode))
        {
            var exists = await _accounts.GetByCodeAsync(tenantId, applicationId, explicitCode, cancellationToken);
            if (exists is not null) return explicitCode;
        }

        if (applicationDefault.HasValue)
        {
            var def = await _accounts.GetDefaultAsync(tenantId, applicationId, applicationDefault.Value, cancellationToken);
            if (def is not null) return applicationDefault.Value;
        }

        return ProviderCode.Fake;
    }
}

public sealed class CreateCheckoutRequestValidator : AbstractValidator<CreateCheckoutRequestDto>
{
    public CreateCheckoutRequestValidator()
    {
        RuleFor(x => x.ExternalReference).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty();
            item.RuleFor(i => i.Name).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitAmount).GreaterThan(0);
        });
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.SuccessUrl).MaximumLength(2000);
        RuleFor(x => x.CancelUrl).MaximumLength(2000);
    }
}
