using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Postgres.Options;

namespace PaymentHub.Worker;

public sealed class OutboxDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherWorker> _logger;
    private readonly PaymentHubOptions _options;

    public OutboxDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherWorker> logger,
        IOptions<PaymentHubOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxDispatcherWorker started (batch={Batch}, interval={Interval}s)",
            _options.OutboxWorkerBatchSize, _options.OutboxWorkerIntervalSeconds);

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.OutboxWorkerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while dispatching outbox events");
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

        _logger.LogInformation("OutboxDispatcherWorker stopped");
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentHubDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IApplicationWebhookDispatcher>();

        var pending = await repository.GetPendingForDispatchAsync(_options.OutboxWorkerBatchSize, cancellationToken);
        if (pending.Count == 0) return;

        _logger.LogInformation("Dispatching {Count} outbox events", pending.Count);

        foreach (var outbox in pending)
        {
            outbox.MarkProcessing();
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                await dispatcher.DispatchAsync(outbox, cancellationToken);
                outbox.MarkSent();
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                var now = DateTime.UtcNow;
                var nextRetry = RetryPolicy.NextRetryAt(outbox.RetryCount + 1, now);
                if (nextRetry is null)
                {
                    _logger.LogError(ex,
                        "Outbox event {OutboxId} permanently failed after {Retries} retries",
                        outbox.Id, outbox.RetryCount + 1);
                    outbox.MarkFailed(ex.Message);
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Outbox event {OutboxId} dispatch failed, retry scheduled at {NextRetry}",
                        outbox.Id, nextRetry);
                    outbox.MarkRetry(ex.Message, nextRetry.Value);
                }
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
