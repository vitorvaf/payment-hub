using System.Text.Json;
using FluentValidation;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Observability;
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
    private readonly IRuntimeEnvironment _environment;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

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
        IClock clock,
        IRuntimeEnvironment environment,
        ICorrelationIdAccessor correlationIdAccessor)
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
        _environment = environment;
        _correlationIdAccessor = correlationIdAccessor;
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

        var requestHash = _requestHasher.Hash(BuildIdempotencyHashInput(request));

        var existing = await _idempotency.FindAsync(tenantId, applicationId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException();

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

        var resolved = await ResolveProviderAsync(
            tenantId, applicationId, application.DefaultProvider, requestedProviderCode, cancellationToken);

        var adapter = _router.Resolve(resolved.ProviderCode.ToString());

        var payment = new Payment(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            request.ExternalReference,
            money,
            resolved.ProviderCode,
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
            request.Items.Select(i => new ProviderCheckoutItem(i.Id, i.Name, i.Quantity, i.UnitAmount)).ToList())
        {
            ProviderAccountId = resolved.ProviderAccountId,
            ProviderEnvironment = resolved.Environment.ToString(),
            ProtectedCredentials = resolved.EncryptedCredentials
        };

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
            requestHash,
            payment.Id);
        await _idempotency.AddAsync(idemKey, cancellationToken);

        var outboxEventId = Guid.NewGuid();
        // Slice 9-O1.2: thread the request-scoped correlation id through to
        // the outbox row so the dispatcher echoes it on the outbound
        // X-Correlation-Id header.
        var correlationId = _correlationIdAccessor.CorrelationId;
        await _outbox.EnqueueAsync(
            outboxEventId,
            tenantId,
            applicationId,
            "payment.checkout.created",
            new
            {
                eventId = outboxEventId,
                eventType = "payment.checkout.created",
                paymentId = payment.Id,
                externalReference = payment.ExternalReference,
                amount = money.Amount,
                currency = money.Currency,
                provider = resolved.ProviderCode.ToString(),
                status = payment.Status.ToString(),
                checkoutUrl = payment.CheckoutUrl,
                providerPaymentId = payment.ProviderPaymentId,
                occurredAt = payment.CreatedAt
            },
            correlationId,
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

    private async Task<ResolvedProvider> ResolveProviderAsync(
        Guid tenantId,
        Guid applicationId,
        ProviderCode? applicationDefault,
        string? requested,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            !Enum.TryParse<ProviderCode>(requested, ignoreCase: true, out _))
        {
            throw new InvalidOperationException($"Provider '{requested}' is not supported.");
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var explicitCode = Enum.Parse<ProviderCode>(requested, ignoreCase: true);
            var account = await _accounts.GetByCodeAsync(tenantId, applicationId, explicitCode, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Provider '{explicitCode}' has no active account for this application.");

            return ResolvedProvider.FromAccount(account);
        }

        if (applicationDefault.HasValue)
        {
            var def = await _accounts.GetDefaultAsync(tenantId, applicationId, applicationDefault.Value, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Default provider '{applicationDefault.Value}' has no active account for this application.");

            return ResolvedProvider.FromAccount(def);
        }

        if (_environment.IsDevelopment)
        {
            // Development fallback: no ProviderAccount persisted yet.
            // Adapters that require credentials (AbacatePay) treat this as
            // a controlled failure in CreateCheckoutProviderResult; Fake
            // continues to work because it ignores credentials.
            return ResolvedProvider.DevFallback(ProviderCode.Fake);
        }

        throw new InvalidOperationException("No default provider configured for this application.");
    }

    private static string BuildIdempotencyHashInput(CreateCheckoutRequestDto request)
    {
        var canonical = new
        {
            externalReference = request.ExternalReference,
            customer = request.Customer is null
                ? null
                : new { name = request.Customer.Name, email = request.Customer.Email },
            items = request.Items.Select(i => new
            {
                id = i.Id,
                name = i.Name,
                quantity = i.Quantity,
                unitAmount = i.UnitAmount
            }).ToArray(),
            currency = string.IsNullOrWhiteSpace(request.Currency) ? "BRL" : request.Currency,
            successUrl = request.SuccessUrl,
            cancelUrl = request.CancelUrl,
            metadata = request.Metadata?
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)
        };

        return JsonSerializer.Serialize(canonical);
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

/// <summary>
/// Internal helper carrying the resolved provider and the matching
/// <c>ProviderAccount</c> so the adapter can unprotect credentials without
/// the handler depending on any HTTP / client layer. Credentials and account
/// id are forwarded to the adapter via <c>CreateCheckoutProviderRequest</c>
/// init-only properties; <see cref="Environment"/> is duplicated there so
/// adapters can branch on sandbox vs production without depending on this
/// type.
/// </summary>
internal sealed record ResolvedProvider(
    ProviderCode ProviderCode,
    Guid ProviderAccountId,
    ProviderEnvironment Environment,
    string EncryptedCredentials)
{
    public static ResolvedProvider FromAccount(ProviderAccount account)
        => new(account.ProviderCode, account.Id, account.Environment, account.EncryptedCredentials);

    /// <summary>
    /// Development-only fallback used when no <c>ProviderAccount</c> is
    /// configured but the environment is Development. Always produces a
    /// credential-less record so the Fake adapter continues to work without
    /// an account; AbacatePay and other credentialed adapters will surface a
    /// controlled failure in their own result.
    /// </summary>
    public static ResolvedProvider DevFallback(ProviderCode code)
        => new(code, Guid.Empty, ProviderEnvironment.Sandbox, string.Empty);
}
