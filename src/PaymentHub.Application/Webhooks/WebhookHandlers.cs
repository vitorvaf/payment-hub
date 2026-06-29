using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
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
    private readonly IProviderAccountRepository _providerAccounts;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IPaymentProviderRouter _router;
    private readonly IOutboxPublisher _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ProcessWebhookEventHandler> _logger;

    public ProcessWebhookEventHandler(
        IWebhookEventRepository webhooks,
        IPaymentRepository payments,
        IProviderAccountRepository providerAccounts,
        ICredentialProtector credentialProtector,
        IPaymentProviderRouter router,
        IOutboxPublisher outbox,
        IUnitOfWork uow,
        IClock clock,
        ILogger<ProcessWebhookEventHandler> logger)
    {
        _webhooks = webhooks;
        _payments = payments;
        _providerAccounts = providerAccounts;
        _credentialProtector = credentialProtector;
        _router = router;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
        _logger = logger;
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

            // Slice 2-B: webhookSecret must be resolved BEFORE the adapter
            // runs HMAC verification. The handler looks up the
            // ProviderAccount by (tenantId, applicationId, providerCode)
            // and unprotects the secret inside EncryptedCredentials. The
            // secret is passed in-memory to the adapter and is NEVER
            // persisted on the WebhookEvent row, NEVER logged, and NEVER
            // returned in error messages.
            //
            // Outcomes:
            // - TryResolveWebhookSecretAsync returns a string  → adapter
            //   receives it via init-only property.
            // - Returns null AND provider is non-AbacatePay → the
            //   provider's adapter does not require HMAC, follow legacy
            //   path with no secret.
            // - Returns null AND provider is AbacatePay → AbacatePay
            //   REQUIRES HMAC, so the webhook is permanently failed.
            string? webhookSecret = null;
            bool requiresHmac = webhook.ProviderCode == ProviderCode.AbacatePay;
            if (requiresHmac)
            {
                webhookSecret = await ResolveAbacatePayWebhookSecretAsync(webhook, cancellationToken);
                if (webhookSecret is null)
                {
                    _logger.LogWarning(
                        "Webhook {WebhookId} for provider {ProviderCode} could not resolve webhookSecret; marking permanently failed.",
                        webhook.Id, webhook.ProviderCode);
                    webhook.MarkPermanentlyFailed(
                        $"Webhook secret could not be resolved for provider '{webhook.ProviderCode}'.");
                    await _uow.SaveChangesAsync(cancellationToken);
                    return;
                }
            }

            var parsed = await adapter.ParseWebhookAsync(
                new ProviderWebhookRequest(
                    webhook.RawPayloadJson,
                    webhook.Signature,
                    new Dictionary<string, string>
                    {
                        ["X-Provider-Event-Id"] = webhook.ProviderEventId ?? string.Empty,
                        ["X-Provider-Event-Type"] = webhook.EventType
                    })
                {
                    // Adapter receives the secret via init-only property.
                    // The handler keeps no field-level reference — once
                    // ParseWebhookAsync returns, the secret reference is
                    // eligible for GC.
                    WebhookSecret = webhookSecret
                },
                cancellationToken);

            if (!parsed.IsValid)
            {
                _logger.LogWarning(
                    "Webhook {WebhookId} for provider {ProviderCode} failed to parse: reason={Reason}.",
                    webhook.Id, webhook.ProviderCode, parsed.ErrorMessage);
                webhook.MarkPermanentlyFailed(
                    Sanitize(parsed.ErrorMessage) ?? "Invalid provider webhook payload.");
                await _uow.SaveChangesAsync(cancellationToken);
                return;
            }

            var providerPaymentId = parsed.ProviderPaymentId;
            if (string.IsNullOrWhiteSpace(providerPaymentId))
                throw new InvalidOperationException("Webhook payload missing provider payment id.");

            var payment = await FindPaymentAsync(webhook, providerPaymentId, cancellationToken);
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
            // Slice 3-IT fix: explicitly add the new PaymentAttempt via the
            // repository. EF Core's collection navigation change detector
            // does NOT pick up items added via the entity's collection
            // property (Payment.Attempts is an IReadOnlyCollection backed
            // by a private List) — they end up tracked as Modified instead
            // of Added, which causes the subsequent UPDATE to match 0 rows
            // because there is no existing row with the new Id. Calling
            // _payments.AddAttemptAsync ensures EF tracks the entity as
            // Added and issues the correct INSERT.
            var attempt = payment.RegisterAttempt(
                ToAttemptStatus(newStatus),
                providerPaymentId,
                null);
            await _payments.AddAttemptAsync(attempt, cancellationToken);

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
            // Slice 2-B: defensively sanitize — make sure we never persist
            // a secret/credential/raw body in LastError, even if the
            // underlying exception somehow bubbles up with one. The
            // underlying message is still logged for triage.
            _logger.LogError(ex,
                "Unexpected error while processing webhook {WebhookId} for provider {ProviderCode}.",
                webhook.Id, webhook.ProviderCode);
            var sanitized = Sanitize(ex.Message) ?? "Unexpected webhook processing error.";
            var nextRetry = RetryPolicy.NextRetryAt(webhook.RetryCount + 1, _clock.UtcNow);
            if (nextRetry is null)
                webhook.MarkPermanentlyFailed(sanitized);
            else
                webhook.MarkFailed(sanitized, nextRetry.Value);
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Resolves the AbacatePay webhook secret by:
    /// (1) parsing metadata from the raw payload (tenantId/applicationId/paymentId),
    /// (2) loading the Payment via (tenantId, paymentId) to confirm
    ///     tenant/application context,
    /// (3) loading the ProviderAccount by (tenantId, applicationId, AbacatePay),
    /// (4) unprotecting the secret out of EncryptedCredentials.
    ///
    /// Returns null when any of the steps cannot be completed. Returning
    /// null signals the caller to permanently fail the webhook — AbacatePay
    /// REQUIRES HMAC for every event, so accepting unsigned events would
    /// open the door to spoofing.
    /// </summary>
    private async Task<string?> ResolveAbacatePayWebhookSecretAsync(
        WebhookEvent webhook,
        CancellationToken cancellationToken)
    {
        // 1. Parse metadata permissively. Bad JSON or missing fields → null.
        var metadata = TryReadAbacatePayMetadata(webhook.RawPayloadJson);

        // 2. Locate the payment by (tenantId, paymentId) and confirm the
        //    application matches. If metadata is missing or the payment
        //    cannot be located, refuse to guess — a tenant-scoping
        //    breach is worse than a 4xx.
        Payment? payment = null;
        if (metadata is not null
            && metadata.TenantId is Guid tid && tid != Guid.Empty
            && metadata.ApplicationId is Guid aid && aid != Guid.Empty
            && metadata.PaymentId is Guid pid && pid != Guid.Empty)
        {
            var direct = await _payments.GetByIdForTenantAsync(tid, pid, cancellationToken);
            if (direct is not null
                && direct.SelectedProvider == webhook.ProviderCode
                && direct.ApplicationId == aid)
            {
                payment = direct;
            }
        }

        if (payment is null)
        {
            _logger.LogWarning(
                "AbacatePay webhook {WebhookId} missing tenant/application/payment metadata or payment lookup failed.",
                webhook.Id);
            return null;
        }

        // 3. Lookup the ProviderAccount scoped to (tenant, application, provider).
        var account = await _providerAccounts.GetByCodeAsync(
            payment.TenantId, payment.ApplicationId, webhook.ProviderCode, cancellationToken);
        if (account is null)
        {
            _logger.LogWarning(
                "AbacatePay webhook {WebhookId} could not find ProviderAccount for tenant={TenantId} application={ApplicationId} provider={ProviderCode}.",
                webhook.Id, payment.TenantId, payment.ApplicationId, webhook.ProviderCode);
            return null;
        }

        // 4. Unprotect credentials and extract the webhookSecret. NEVER
        //    log the unprotected blob, the apiKey, or the webhookSecret.
        return ExtractWebhookSecret(account, webhook.Id);
    }

    private string? ExtractWebhookSecret(ProviderAccount account, Guid webhookId)
    {
        string plain;
        try
        {
            plain = _credentialProtector.Unprotect(account.EncryptedCredentials);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Webhook {WebhookId} ProviderAccount {ProviderAccountId} credentials could not be unprotected.",
                webhookId, account.Id);
            return null;
        }

        if (string.IsNullOrWhiteSpace(plain))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(plain);
            var root = doc.RootElement;
            // Prefer the explicit field; fall back to legacy "secret".
            string? webhookSecret = null;
            if (root.TryGetProperty("webhookSecret", out var ws) && ws.ValueKind == JsonValueKind.String)
                webhookSecret = ws.GetString();
            else if (root.TryGetProperty("secret", out var s) && s.ValueKind == JsonValueKind.String)
                webhookSecret = s.GetString();

            return string.IsNullOrWhiteSpace(webhookSecret) ? null : webhookSecret;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Webhook {WebhookId} ProviderAccount {ProviderAccountId} credentials are not valid JSON.",
                webhookId, account.Id);
            return null;
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

    /// <summary>
    /// Defensive sanitization of error text BEFORE it is persisted on
    /// <c>WebhookEvent.LastError</c>. Caps length and strips characters
    /// known to be unsafe (newlines, NULs, control chars) so that the
    /// row can never serve as a back-channel for sensitive content.
    /// </summary>
    internal static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var trimmed = value.Replace("\r", " ").Replace("\n", " ").Replace('\0', ' ');
        return trimmed.Length <= 2000 ? trimmed : trimmed[..2000];
    }

    /// <summary>
    /// Permissive read of <c>data.metadata.{tenantId, applicationId,
    /// paymentId}</c> from a raw AbacatePay v2 webhook payload. Returns
    /// null when any required field is missing or malformed. Never
    /// throws — bad JSON is treated as missing metadata so the caller
    /// can fall through to alternative routing.
    /// </summary>
    private static AbacatePayMetadata? TryReadAbacatePayMetadata(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("metadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            Guid? tenantId = ReadGuid(metadata, "tenantId");
            Guid? applicationId = ReadGuid(metadata, "applicationId");
            Guid? paymentId = ReadGuid(metadata, "paymentId");
            if (tenantId is null || applicationId is null || paymentId is null)
                return null;

            return new AbacatePayMetadata(tenantId, applicationId, paymentId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? ReadGuid(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            return Guid.TryParse(s, out var g) ? g : null;
        }
        return null;
    }

    private sealed record AbacatePayMetadata(Guid? TenantId, Guid? ApplicationId, Guid? PaymentId);
}
