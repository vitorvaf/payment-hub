using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Webhooks;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Postgres.Options;

namespace PaymentHub.Worker;

public sealed class WebhookProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookProcessorWorker> _logger;
    private readonly PaymentHubOptions _options;

    public WebhookProcessorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookProcessorWorker> logger,
        IOptions<PaymentHubOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WebhookProcessorWorker started (batch={Batch}, interval={Interval}s)",
            _options.WebhookWorkerBatchSize, _options.WebhookWorkerIntervalSeconds);

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.WebhookWorkerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while processing webhook events");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("WebhookProcessorWorker stopped");
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentHubDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IProcessWebhookEventHandler>();

        var pending = await db.WebhookEvents
            .Where(w => w.ProcessingStatus == Domain.Enums.WebhookProcessingStatus.Pending
                        && (w.NextRetryAt == null || w.NextRetryAt <= DateTime.UtcNow))
            .OrderBy(w => w.ReceivedAt)
            .Take(_options.WebhookWorkerBatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return;

        _logger.LogInformation("Processing {Count} webhook events", pending.Count);

        foreach (var webhook in pending)
        {
            try
            {
                await handler.ProcessAsync(webhook.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process webhook event {WebhookId}", webhook.Id);
            }
        }
    }
}
