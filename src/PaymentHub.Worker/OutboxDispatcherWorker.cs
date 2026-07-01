using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;
using PaymentHub.Infrastructure.Postgres.Options;

namespace PaymentHub.Worker;

/// <summary>
/// Polls the outbox table for pending events and dispatches each via
/// <see cref="IApplicationWebhookDispatcher"/>. Slice 7-A removes the direct dependency on the
/// EF Core <c>DbContext</c> by going through <see cref="IOutboxRepository"/> (read) and
/// <see cref="IOutboxEventStore"/> (write); the current UTC time is sourced from
/// <see cref="IClock"/> so retry schedules are deterministic in tests.
/// </summary>
public sealed class OutboxDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<OutboxDispatcherWorker> _logger;
    private readonly PaymentHubOptions _options;

    public OutboxDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<OutboxDispatcherWorker> logger,
        IOptions<PaymentHubOptions> options)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
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

    /// <summary>
    /// Single dispatch iteration. <c>internal</c> so unit tests can drive one iteration
    /// with a real (in-memory) service provider without having to spin up
    /// <see cref="ExecuteAsync"/>. Production code always reaches it through the hosted
    /// service loop.
    /// </summary>
    /// <remarks>
    /// Slice 7-M1: the iteration now consumes the atomic <c>ClaimPendingForDispatchAsync</c>
    /// (SELECT ... FOR UPDATE SKIP LOCKED + UPDATE in one transaction). The returned entities
    /// are already in <c>Processing</c> with <c>ProcessingStartedAt</c> set, so the worker no
    /// longer calls <see cref="OutboxEvent.MarkProcessing()"/> separately. The orphan sweep
    /// (<c>SweepOrphanedProcessingAsync</c>) is wired in 7-M1.4.
    /// </remarks>
    internal async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        // Snapshot the clock once per iteration so every retry schedule in this batch is
        // computed against the same "now". Avoids sub-millisecond jitter between events
        // and keeps the retry policy deterministic in tests. Also used as the
        // `processing_started_at` stamp persisted by ClaimPendingForDispatchAsync.
        var now = _clock.UtcNow;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var eventStore = scope.ServiceProvider.GetRequiredService<IOutboxEventStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IApplicationWebhookDispatcher>();

        // Slice 7-M1.4: orphan sweep runs BEFORE the claim so a worker restart can recover
        // any rows the previous instance left in `Processing` past the configured TTL, and
        // then proceed to dispatch them in the same iteration when the backoff window allows.
        var cutoff = now.AddSeconds(-Math.Max(0, _options.OutboxProcessingTimeoutSeconds));
        var sweepRecovered = await repository.SweepOrphanedProcessingAsync(cutoff, cancellationToken);
        if (sweepRecovered > 0)
        {
            _logger.LogInformation(
                "OutboxDispatcherWorker orphan sweep recovered {Recovered} Processing rows past the {Timeout}s TTL",
                sweepRecovered, _options.OutboxProcessingTimeoutSeconds);
        }

        var claimed = await repository.ClaimPendingForDispatchAsync(
            _options.OutboxWorkerBatchSize, now, cancellationToken);
        if (claimed.Count == 0) return;

        _logger.LogInformation("Dispatching {Count} outbox events", claimed.Count);

        foreach (var outbox in claimed)
        {
            // Sanity: claim must have already flipped the row to Processing with a non-null
            // ProcessingStartedAt. If a future regression removes the UPDATE from the claim
            // path the worker must NOT silently mark it Processing again — that would
            // re-introduce the double-dispatch race the slice closes.
            if (outbox.Status != OutboxEventStatus.Processing || outbox.ProcessingStartedAt is null)
            {
                _logger.LogError(
                    "Outbox event {OutboxId} was returned by ClaimPendingForDispatchAsync in an invalid state (status={Status}, processingStartedAt={ProcessingStartedAt}). Skipping dispatch to avoid a regression of the multi-instance race.",
                    outbox.Id, outbox.Status, outbox.ProcessingStartedAt);
                continue;
            }

            try
            {
                await dispatcher.DispatchAsync(outbox, cancellationToken);
                outbox.MarkSent();
                await eventStore.SaveAsync(outbox, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Slice 7-A.8: re-throw cancellation so ExecuteAsync can break its loop
                // cleanly. Without this, OCE from DispatchAsync would fall through to the
                // generic catch and be classified as UnexpectedDispatcherError, which would
                // both silently swallow the cancel signal and pollute LastError.
                throw;
            }
            catch (WebhookDispatcherException wex)
            {
                // Slice 7-A.7: only Category + StatusCode are persisted to LastError. The raw
                // exception (and its message) goes to structured logs for ops triage.
                var nextRetry = RetryPolicy.NextRetryAt(outbox.RetryCount + 1, now);
                if (nextRetry is null)
                {
                    _logger.LogError(wex,
                        "Outbox event {OutboxId} permanently failed after {Retries} retries (category={Category}, status={StatusCode})",
                        outbox.Id, outbox.RetryCount + 1, wex.Category, wex.StatusCode);
                    ApplyFailure(outbox, wex, nextAttemptAt: null);
                }
                else
                {
                    _logger.LogWarning(wex,
                        "Outbox event {OutboxId} dispatch failed (category={Category}, status={StatusCode}), retry scheduled at {NextRetry}",
                        outbox.Id, wex.Category, wex.StatusCode, nextRetry);
                    ApplyFailure(outbox, wex, nextAttemptAt: nextRetry.Value);
                }
                await eventStore.SaveAsync(outbox, cancellationToken);
            }
            catch (Exception ex)
            {
                // Unexpected (non-WebhookDispatcherException) failures: categorise as
                // UnexpectedDispatcherError. We deliberately do NOT persist ex.Message.
                var nextRetry = RetryPolicy.NextRetryAt(outbox.RetryCount + 1, now);
                if (nextRetry is null)
                {
                    _logger.LogError(ex,
                        "Outbox event {OutboxId} permanently failed after {Retries} retries with unexpected error",
                        outbox.Id, outbox.RetryCount + 1);
                    outbox.MarkFailedWithCategory(WebhookDispatcherCategory.UnexpectedDispatcherError);
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Outbox event {OutboxId} dispatch failed with unexpected error, retry scheduled at {NextRetry}",
                        outbox.Id, nextRetry);
                    outbox.MarkRetryWithCategory(WebhookDispatcherCategory.UnexpectedDispatcherError, nextRetry.Value);
                }
                await eventStore.SaveAsync(outbox, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Applies the safe failure transition (retry or terminal failure) to <paramref name="outbox"/>
    /// based on the dispatcher's <see cref="WebhookDispatcherException"/>. Never persists the
    /// exception's <c>Message</c>: only the structured category and HTTP status code land in
    /// <c>OutboxEvent.LastError</c>.
    /// </summary>
    private void ApplyFailure(OutboxEvent outbox, WebhookDispatcherException wex, DateTime? nextAttemptAt)
    {
        if (wex.StatusCode is int statusCode)
        {
            if (nextAttemptAt is DateTime retryAt)
                outbox.MarkRetryWithStatus(wex.Category, statusCode, retryAt);
            else
                outbox.MarkFailedWithStatus(wex.Category, statusCode);
        }
        else
        {
            if (nextAttemptAt is DateTime retryAt)
                outbox.MarkRetryWithCategory(wex.Category, retryAt);
            else
                outbox.MarkFailedWithCategory(wex.Category);
        }
    }
}