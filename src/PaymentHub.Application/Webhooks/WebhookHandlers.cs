using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;

namespace PaymentHub.Application.Webhooks;

public interface IReceiveProviderWebhookHandler
{
    Task<Guid> HandleAsync(
        string providerCode,
        string eventType,
        string rawBody,
        string? providerEventId,
        string? signature,
        CancellationToken cancellationToken);
}

public interface IProcessWebhookEventHandler
{
    Task ProcessAsync(Guid webhookEventId, CancellationToken cancellationToken);
}

public sealed class ReceiveProviderWebhookHandler : IReceiveProviderWebhookHandler
{
    private readonly IWebhookEventRepository _webhooks;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ReceiveProviderWebhookHandler(
        IWebhookEventRepository webhooks,
        IUnitOfWork uow,
        IClock clock)
    {
        _webhooks = webhooks;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Guid> HandleAsync(
        string providerCode,
        string eventType,
        string rawBody,
        string? providerEventId,
        string? signature,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ProviderCode>(providerCode, ignoreCase: true, out var code))
            throw new InvalidOperationException($"Unknown provider '{providerCode}'.");

        if (!string.IsNullOrWhiteSpace(providerEventId))
        {
            var existing = await _webhooks.GetByProviderEventIdAsync(code.ToString(), providerEventId, cancellationToken);
            if (existing is not null) return existing.Id;
        }

        var webhook = new WebhookEvent(
            Guid.NewGuid(),
            code,
            eventType,
            rawBody,
            providerEventId,
            signature);

        await _webhooks.AddAsync(webhook, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return webhook.Id;
    }
}

public sealed class ProcessWebhookEventHandler : IProcessWebhookEventHandler
{
    private readonly IWebhookEventRepository _webhooks;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderRouter _router;
    private readonly IOutboxPublisher _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ProcessWebhookEventHandler(
        IWebhookEventRepository webhooks,
        IPaymentRepository payments,
        IPaymentProviderRouter router,
        IOutboxPublisher outbox,
        IUnitOfWork uow,
        IClock clock)
    {
        _webhooks = webhooks;
        _payments = payments;
        _router = router;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task ProcessAsync(Guid webhookEventId, CancellationToken cancellationToken)
    {
        var webhook = await _webhooks.GetByIdAsync(webhookEventId, cancellationToken)
            ?? throw new InvalidOperationException($"Webhook event {webhookEventId} not found.");

        if (webhook.ProcessingStatus == WebhookProcessingStatus.Processed)
            return;

        webhook.MarkProcessing();
        await _uow.SaveChangesAsync(cancellationToken);

        try
        {
            var adapter = _router.Resolve(webhook.ProviderCode.ToString());
            var parsed = await adapter.ParseWebhookAsync(
                new ProviderWebhookRequest(
                    webhook.RawPayloadJson,
                    webhook.Signature,
                    new Dictionary<string, string>
                    {
                        ["X-Provider-Event-Id"] = webhook.ProviderEventId ?? string.Empty,
                        ["X-Provider-Event-Type"] = webhook.EventType
                    }),
                cancellationToken);

            if (!parsed.IsValid)
            {
                webhook.MarkPermanentlyFailed(parsed.ErrorMessage ?? "Invalid provider webhook payload.");
                await _uow.SaveChangesAsync(cancellationToken);
                return;
            }

            var providerPaymentId = parsed.ProviderPaymentId;
            if (string.IsNullOrWhiteSpace(providerPaymentId))
                throw new InvalidOperationException("Webhook payload missing provider payment id.");

            var payments = await FindPaymentAsync(webhook, providerPaymentId, cancellationToken);
            var payment = payments;
            if (payment is null)
            {
                webhook.MarkPermanentlyFailed(
                    $"Payment not found for provider '{webhook.ProviderCode}' and providerPaymentId '{providerPaymentId}'.");
                await _uow.SaveChangesAsync(cancellationToken);
                return;
            }

            var newStatus = PaymentStatusMapper.FromProviderStatus(webhook.ProviderCode.ToString(), parsed.ProviderStatus ?? parsed.EventType);
            var previousStatus = payment.Status;
            var statusChanged = payment.ApplyProviderStatus(newStatus, providerPaymentId);
            payment.RegisterAttempt(
                ToAttemptStatus(newStatus),
                providerPaymentId,
                null);

            if (statusChanged && previousStatus != newStatus)
            {
                var eventType = $"payment.{newStatus.ToString().ToLowerInvariant()}";
                var outboxEventId = Guid.NewGuid();
                await _outbox.EnqueueAsync(
                    outboxEventId,
                    payment.TenantId,
                    payment.ApplicationId,
                    eventType,
                    new
                    {
                        eventId = outboxEventId,
                        eventType,
                        paymentId = payment.Id,
                        externalReference = payment.ExternalReference,
                        amount = payment.Amount.Amount,
                        currency = payment.Currency,
                        provider = payment.SelectedProvider.ToString(),
                        status = payment.Status.ToString(),
                        providerPaymentId = payment.ProviderPaymentId,
                        occurredAt = payment.UpdatedAt
                    },
                    cancellationToken);
            }

            webhook.AssociateTenant(payment.TenantId, payment.ApplicationId);
            webhook.MarkProcessed();
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var nextRetry = RetryPolicy.NextRetryAt(webhook.RetryCount + 1, _clock.UtcNow);
            if (nextRetry is null)
                webhook.MarkPermanentlyFailed(ex.Message);
            else
                webhook.MarkFailed(ex.Message, nextRetry.Value);
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Payment?> FindPaymentAsync(WebhookEvent webhook, string providerPaymentId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(providerPaymentId, out var paymentId))
        {
            var direct = await _payments.GetByIdAsync(paymentId, cancellationToken);
            if (direct is not null && direct.SelectedProvider == webhook.ProviderCode)
                return direct;
        }
        return await _payments.GetByProviderPaymentIdAsync(webhook.ProviderCode.ToString(), providerPaymentId, cancellationToken);
    }

    private static PaymentAttemptStatus ToAttemptStatus(PaymentStatus status)
        => status switch
        {
            PaymentStatus.Approved or PaymentStatus.Refunded or PaymentStatus.Chargeback => PaymentAttemptStatus.Succeeded,
            PaymentStatus.Rejected or PaymentStatus.Cancelled or PaymentStatus.Expired or PaymentStatus.Failed => PaymentAttemptStatus.Failed,
            _ => PaymentAttemptStatus.Pending
        };
}
