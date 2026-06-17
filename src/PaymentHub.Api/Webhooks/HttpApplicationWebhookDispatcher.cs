using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Entities;
using PaymentHub.Infrastructure.Postgres.Options;

namespace PaymentHub.Api.Webhooks;

public sealed class HttpApplicationWebhookDispatcher : IApplicationWebhookDispatcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationClientRepository _apps;
    private readonly IWebhookSigner _signer;
    private readonly ILogger<HttpApplicationWebhookDispatcher> _logger;
    private readonly PaymentHubOptions _options;

    public HttpApplicationWebhookDispatcher(
        IHttpClientFactory httpClientFactory,
        IApplicationClientRepository apps,
        IWebhookSigner signer,
        ILogger<HttpApplicationWebhookDispatcher> logger,
        IOptions<PaymentHubOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _apps = apps;
        _signer = signer;
        _logger = logger;
        _options = options.Value;
    }

    public async Task DispatchAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var app = await _apps.GetByIdAsync(outboxEvent.ApplicationId, cancellationToken);
        if (app is null || string.IsNullOrWhiteSpace(app.WebhookUrl))
        {
            _logger.LogWarning(
                "Skipping outbox event {OutboxEventId}: application {ApplicationId} has no webhook url",
                outboxEvent.Id, outboxEvent.ApplicationId);
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = string.IsNullOrWhiteSpace(app.WebhookSecret)
            ? string.Empty
            : _signer.Sign(outboxEvent.PayloadJson, app.WebhookSecret, timestamp);

        var client = _httpClientFactory.CreateClient("application-webhook");
        client.Timeout = TimeSpan.FromSeconds(_options.WebhookHttpTimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, app.WebhookUrl)
        {
            Content = new StringContent(outboxEvent.PayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-PaymentHub-Event", outboxEvent.EventType);
        request.Headers.TryAddWithoutValidation("X-PaymentHub-Event-Type", outboxEvent.EventType);
        request.Headers.TryAddWithoutValidation("X-PaymentHub-Event-Id", outboxEvent.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-PaymentHub-Tenant", outboxEvent.TenantId.ToString());
        request.Headers.TryAddWithoutValidation("X-PaymentHub-Application", outboxEvent.ApplicationId.ToString());
        request.Headers.TryAddWithoutValidation("X-PaymentHub-Timestamp", timestamp);
        if (!string.IsNullOrEmpty(signature))
        {
            request.Headers.TryAddWithoutValidation("X-PaymentHub-Signature", signature);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Application webhook responded {(int)response.StatusCode}: {Truncate(body, 500)}");
        }
    }

    private static string Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? string.Empty :
            value.Length <= maxLength ? value : value[..maxLength];
}
