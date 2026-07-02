using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Observability;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres.Options;

namespace PaymentHub.Infrastructure.Postgres.Webhooks;

/// <summary>
/// HTTP implementation of <see cref="IApplicationWebhookDispatcher"/>. Located in
/// <c>Infrastructure.Postgres</c> so it can be reused by both the API host and the Worker host
/// (Slice 7-A resolves gap P1-4 — the Worker used to register a Noop dispatcher).
///
/// Security invariants (see <c>docs/specs/011-security-and-compliance.md</c> and ADR-0010):
/// - The application lookup is scoped by both <c>tenantId</c> and <c>applicationId</c> via
///   <see cref="IApplicationClientRepository.GetByTenantAndIdAsync"/>, so a malformed
///   <c>OutboxEvent</c> with mismatched ids cannot dispatch to another tenant's URL.
/// - The protected webhook secret is unprotected in memory immediately before HMAC signing and
///   never logged. If <c>Unprotect</c> fails, the dispatcher throws
///   <see cref="WebhookDispatcherException"/> with category <see cref="WebhookDispatcherCategory.UnprotectFailure"/>
///   and aborts the dispatch (it does NOT send the request unsigned).
/// - HTTP non-2xx responses raise a <see cref="WebhookDispatcherException"/> WITHOUT including
///   the response body in the message. The worker reads <c>Category</c> + <c>StatusCode</c> from
///   the exception to populate <c>OutboxEvent.LastError</c>; <c>ex.Message</c> is never used
///   to build the persisted error.
/// </summary>
public sealed class HttpApplicationWebhookDispatcher : IApplicationWebhookDispatcher
{
    private const string HttpClientName = "application-webhook";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationClientRepository _apps;
    private readonly IWebhookSigner _signer;
    private readonly IWebhookSecretProtector _webhookSecretProtector;
    private readonly ILogger<HttpApplicationWebhookDispatcher> _logger;
    private readonly PaymentHubOptions _options;

    public HttpApplicationWebhookDispatcher(
        IHttpClientFactory httpClientFactory,
        IApplicationClientRepository apps,
        IWebhookSigner signer,
        IWebhookSecretProtector webhookSecretProtector,
        ILogger<HttpApplicationWebhookDispatcher> logger,
        IOptions<PaymentHubOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _apps = apps;
        _signer = signer;
        _webhookSecretProtector = webhookSecretProtector;
        _logger = logger;
        _options = options.Value;
    }

    public async Task DispatchAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        // Slice 9-O2: end-to-end dispatch latency (including repository lookup,
        // unprotect, signature, HTTP send, drain). Recorded via finally so
        // success + failure paths are captured consistently.
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            // Tenant guard (B1): resolve the application inside its tenant. A malformed OutboxEvent
            // with mismatched tenantId/applicationId will miss the repository and we skip the dispatch.
            var app = await _apps.GetByTenantAndIdAsync(
                outboxEvent.TenantId, outboxEvent.ApplicationId, cancellationToken);

            if (app is null)
            {
                _logger.LogWarning(
                    "{Event} outboxId={OutboxId} applicationId={ApplicationId}",
                    PaymentHubLogEvents.OutboxEventApplicationNotFound,
                    SafeLog.Id(outboxEvent.Id), SafeLog.Id(outboxEvent.ApplicationId));
                throw new WebhookDispatcherException(
                    WebhookDispatcherCategory.MissingWebhookUrl,
                    $"Application {outboxEvent.ApplicationId} not found under tenant {outboxEvent.TenantId}.");
            }

            if (string.IsNullOrWhiteSpace(app.WebhookUrl))
            {
                _logger.LogWarning(
                    "{Event} outboxId={OutboxId} applicationId={ApplicationId}",
                    PaymentHubLogEvents.OutboxEventWebhookUrlMissing,
                    SafeLog.Id(outboxEvent.Id), SafeLog.Id(outboxEvent.ApplicationId));
                throw new WebhookDispatcherException(
                    WebhookDispatcherCategory.MissingWebhookUrl,
                    $"Application {app.Id} has no webhook url configured.");
            }

            string timestamp;
            string signature;
            try
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                signature = string.Empty;
                if (app.HasWebhookSecret)
                {
                    var rawSecret = _webhookSecretProtector.Unprotect(app.WebhookSecret!);
                    signature = _signer.Sign(outboxEvent.PayloadJson, rawSecret, timestamp);
                }
            }
            catch (WebhookDispatcherException)
            {
                // Pass through so the worker maps UnprotectFailure directly.
                throw;
            }
            catch (Exception ex)
            {
                // Unprotect/protect/signing failures are categorised as UnprotectFailure; we never
                // leak the exception's message or the underlying blob to LastError or logs.
                _logger.LogError(ex,
                    "{Event} outboxId={OutboxId} applicationId={ApplicationId}",
                    PaymentHubLogEvents.OutboxEventUnprotectFailure,
                    SafeLog.Id(outboxEvent.Id), SafeLog.Id(outboxEvent.ApplicationId));
                throw new WebhookDispatcherException(
                    WebhookDispatcherCategory.UnprotectFailure,
                    "Webhook secret cannot be unprotected.");
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(_options.WebhookHttpTimeoutSeconds);

            using var request = new HttpRequestMessage(HttpMethod.Post, app.WebhookUrl)
            {
                Content = new StringContent(outboxEvent.PayloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-PaymentHub-Event-Type", outboxEvent.EventType);
            request.Headers.TryAddWithoutValidation("X-PaymentHub-Event-Id", outboxEvent.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-PaymentHub-Timestamp", timestamp);
            // Slice 9-O1.2: echo the originating correlation id so consumers can
            // stitch their logs back to the Payment Hub request that produced
            // the outbox row. Outbound header is bounded by the same
            // IsValid window the middleware uses (we already persisted only
            // valid candidates in the column).
            if (!string.IsNullOrEmpty(outboxEvent.CorrelationId))
            {
                request.Headers.TryAddWithoutValidation(CorrelationIdGenerator.HeaderName, outboxEvent.CorrelationId);
            }
            if (!string.IsNullOrEmpty(signature))
            {
                request.Headers.TryAddWithoutValidation("X-PaymentHub-Signature", signature);
            }

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout fires as TaskCanceledException. We only treat it as a webhook
                // timeout when the caller (worker loop) did not cancel; if the caller did cancel,
                // OperationCanceledException is the right signal.
                _logger.LogWarning(ex,
                    "{Event} outboxId={OutboxId} applicationId={ApplicationId}",
                    PaymentHubLogEvents.OutboxEventDispatchTimeout,
                    SafeLog.Id(outboxEvent.Id), SafeLog.Id(outboxEvent.ApplicationId));
                throw new WebhookDispatcherException(
                    WebhookDispatcherCategory.Timeout,
                    "Webhook dispatch timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "{Event} outboxId={OutboxId} applicationId={ApplicationId}",
                    PaymentHubLogEvents.OutboxEventDispatchNetworkError,
                    SafeLog.Id(outboxEvent.Id), SafeLog.Id(outboxEvent.ApplicationId));
                throw new WebhookDispatcherException(
                    WebhookDispatcherCategory.NetworkError,
                    "Network error while dispatching webhook.");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    // Do not include the consumer's response body in the exception message. The body
                    // could carry sensitive content (consumer-controlled) and would otherwise be
                    // persisted to OutboxEvent.LastError by the worker.
                    _logger.LogWarning(
                        "{Event} outboxId={OutboxId} statusCode={StatusCode}",
                        PaymentHubLogEvents.OutboxEventDispatchHttpFailure,
                        SafeLog.Id(outboxEvent.Id), statusCode);
                    throw new WebhookDispatcherException(
                        WebhookDispatcherCategory.HttpFailure,
                        statusCode,
                        $"Application webhook responded {statusCode} (consumer returned non-success).");
                }

                _logger.LogInformation(
                    "{Event} outboxId={OutboxId} statusCode={StatusCode}",
                    PaymentHubLogEvents.OutboxEventSent,
                    SafeLog.Id(outboxEvent.Id), (int)response.StatusCode);
            }
        }
        finally
        {
            // Slice 9-O2: record dispatch latency in the existing
            // OutboxDispatchDurationMs histogram (no new instrument added —
            // the metric was already part of Slice 9-O1's catalogue).
            PaymentHubMetrics.OutboxDispatchDurationMs.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }
}