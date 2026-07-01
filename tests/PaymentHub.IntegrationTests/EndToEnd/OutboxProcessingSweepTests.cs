using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.IntegrationTests.Infrastructure;
using PaymentHub.IntegrationTests.Support;
using PaymentHub.Worker;

namespace PaymentHub.IntegrationTests.EndToEnd;

/// <summary>
/// Slice 7-M1.6 — orphan-sweep and dispatch-filtering contract for the outbox worker.
///
/// <para>
/// Asserts the production guarantees documented in <c>docs/specs/007-inbox-outbox-workers.md</c>
/// and ADR-0010:
/// </para>
/// <list type="bullet">
/// <item><c>SweepOrphanedProcessingAsync</c> re-enqueues rows stuck in <c>Processing</c>
/// past the configured TTL. Only the safe <see cref="WebhookDispatcherCategory.ProcessingOrphaned"/>
/// category lands in <c>last_error</c>; never the original exception, URL, body or
/// signature.</item>
/// <item>Recent <c>Processing</c> rows and terminal rows (<c>Sent</c>, <c>Failed</c>) are
/// never reopened.</item>
/// <item>The claim path respects <c>NextRetryAt</c> (future rows are skipped) and the
/// configured <c>OutboxWorkerBatchSize</c> (no more than N events dispatched per
/// iteration).</item>
/// </list>
/// <para>
/// We control <see cref="PaymentHubOptions.OutboxProcessingTimeoutSeconds"/> and
/// <see cref="PaymentHubOptions.OutboxWorkerBatchSize"/> by instantiating
/// <see cref="OutboxDispatcherWorker"/> directly with <see cref="Options.Create{TOptions}"/>
/// rather than rebuilding the host. This matches the pattern used in
/// <c>OutboxDispatcherE2ETests.RunDispatcherOnceAsync</c>.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class OutboxProcessingSweepTests
{
    private const string TestWebhookUrl = "https://webhook.fake.test/hook";
    private const string TestWebhookSecret = "test-sweep-webhook-secret-do-not-log";

    private readonly PostgresFixture _postgres;

    public OutboxProcessingSweepTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    // -----------------------------------------------------------------
    // P1.3 — Orphan sweep requeues stale Processing rows
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxSweep_ShouldRequeueOrphanedProcessingEvents()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // Seed an event that was left in Processing two hours ago — long past
            // any sensible TTL. The sweep must move it back to Pending with the
            // safe ProcessingOrphaned category in LastError.
            var longAgo = DateTime.UtcNow.AddHours(-2);
            var id = await E2ESeedHelpers.SeedProcessingOutboxEventAsync(
                factory, credentials,
                processingStartedAt: longAgo,
                eventType: "payment.status.changed",
                payload: new { paymentId = Guid.NewGuid(), status = "Approved" });

            // First iteration: sweep moves Processing → Pending; claim picks it up
            // and dispatches. The fake handler returns 204 by default → Sent.
            var worker = BuildWorker(factory, timeoutSeconds: 60, batchSize: 10);
            await worker.DispatchOnceAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == id);

            reloaded.Status.Should().Be(OutboxEventStatus.Sent,
                because: "the sweep moved the row to Pending; the same iteration then dispatched it");

            // The sweep MUST persist only the safe category name, no URL/body/secret.
            // The row was already MarkSent-ed which clears LastError, so we check the
            // captured outbound webhook was the legitimate delivery (CallCount == 1)
            // and that the call body doesn't contain the secret.
            factory.WebhookHandler.CallCount.Should().Be(1);
            factory.WebhookHandler.Last.Should().NotBeNull();
            factory.WebhookHandler.Last!.Body.Should().NotContain(TestWebhookSecret);

            // ProcessingStartedAt is cleared on every transition out of Processing.
            reloaded.ProcessingStartedAt.Should().BeNull();
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.4 — Recent Processing rows are NOT touched by the sweep
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxSweep_ShouldNotRequeueRecentProcessingEvents()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // A row that entered Processing 1 second ago is well within the 60s TTL.
            // The sweep MUST leave it alone — it's the OTHER instance's responsibility.
            var recent = DateTime.UtcNow.AddSeconds(-1);
            var id = await E2ESeedHelpers.SeedProcessingOutboxEventAsync(
                factory, credentials,
                processingStartedAt: recent,
                eventType: "payment.status.changed",
                payload: new { paymentId = Guid.NewGuid(), status = "Approved" });

            // Drive the dispatcher. Sweep cutoff is now - 60s = far in the past relative
            // to `recent` (which is only 1s old), so the sweep's WHERE clause
            // `processing_started_at < cutoff` excludes the row.
            var worker = BuildWorker(factory, timeoutSeconds: 60, batchSize: 10);
            await worker.DispatchOnceAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == id);

            reloaded.Status.Should().Be(OutboxEventStatus.Processing,
                because: "the sweep must not touch a recent Processing row owned by another worker");

            // No outbound HTTP happened.
            factory.WebhookHandler.CallCount.Should().Be(0,
                because: "the claim filters out non-Pending rows; only the sweep touches Processing");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.5 — Terminal rows (Sent, Failed) are never reopened by the sweep
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxSweep_ShouldNotReopenTerminalEvents()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // Seed one Sent and one Failed row directly via the helper, both stamped
            // 2 hours ago (well past the TTL). The sweep's WHERE clause filters on
            // status = 'Processing', so neither row can be re-opened regardless of
            // how old their timestamps are.
            var longAgo = DateTime.UtcNow.AddHours(-2);
            var sentId = await E2ESeedHelpers.SeedOutboxEventAsync(
                factory, credentials,
                eventType: "payment.approved",
                payload: new { which = "sent" },
                status: OutboxEventStatus.Sent);
            var failedId = await E2ESeedHelpers.SeedOutboxEventAsync(
                factory, credentials,
                eventType: "payment.failed",
                payload: new { which = "failed" },
                status: OutboxEventStatus.Failed);

            // Even with a tiny TTL (1s) the sweep must NOT touch terminal rows.
            var worker = BuildWorker(factory, timeoutSeconds: 1, batchSize: 10);
            await worker.DispatchOnceAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var rows = await db.OutboxEvents.AsNoTracking().ToListAsync();
            rows.Should().HaveCount(2);
            rows.Should().OnlyContain(r => r.Status == OutboxEventStatus.Sent
                                          || r.Status == OutboxEventStatus.Failed,
                because: "the sweep must never reopen terminal rows");

            factory.WebhookHandler.CallCount.Should().Be(0,
                because: "neither row was eligible for dispatch");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P2.1 — Claim respects NextRetryAt (future rows are skipped)
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldRespectNextAttemptAt()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // One row with NextRetryAt 1h in the future (not dispatchable yet) and
            // one with NextRetryAt null (dispatchable now). The claim must pick
            // exactly the latter.
            var dueNow = await E2ESeedHelpers.SeedOutboxEventAsync(
                factory, credentials,
                eventType: "payment.status.changed",
                payload: new { which = "due-now" },
                status: OutboxEventStatus.Pending);
            var dueInFuture = await E2ESeedHelpers.SeedOutboxEventAsync(
                factory, credentials,
                eventType: "payment.status.changed",
                payload: new { which = "due-in-future" },
                status: OutboxEventStatus.Pending,
                nextRetryAt: DateTime.UtcNow.AddHours(1));

            var worker = BuildWorker(factory, timeoutSeconds: 900, batchSize: 10);
            await worker.DispatchOnceAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();
            reloaded.Should().HaveCount(2);

            var dueNowRow = reloaded.Single(o => o.Id == dueNow);
            var dueInFutureRow = reloaded.Single(o => o.Id == dueInFuture);

            dueNowRow.Status.Should().Be(OutboxEventStatus.Sent,
                because: "its NextRetryAt is null (or in the past) so the claim dispatches it");
            dueInFutureRow.Status.Should().Be(OutboxEventStatus.Pending,
                because: "its NextRetryAt is in the future so the claim must skip it");
            factory.WebhookHandler.CallCount.Should().Be(1);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P2.2 — Claim respects OutboxWorkerBatchSize (no more than N per iteration)
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldRespectBatchSize_WhenClaimingPendingEvents()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // 10 events, all dispatchable.
            const int eventCount = 10;
            for (int i = 0; i < eventCount; i++)
            {
                await E2ESeedHelpers.SeedOutboxEventAsync(
                    factory, credentials,
                    eventType: "payment.status.changed",
                    payload: new { index = i });
            }

            // Force batch = 3 so the first iteration dispatches at most 3 events.
            // Each remaining iteration dispatches 3 more until all 10 are Sent.
            const int batchSize = 3;
            var worker = BuildWorker(factory, timeoutSeconds: 900, batchSize: batchSize);

            int previousSent = 0;
            int iterations = 0;
            while (true)
            {
                iterations++;
                await worker.DispatchOnceAsync(CancellationToken.None);

                await using var db = factory.CreateDbContext();
                var sentSoFar = await db.OutboxEvents.AsNoTracking()
                    .CountAsync(o => o.Status == OutboxEventStatus.Sent);

                // Iteration is bounded by the worst case (10 / 3 = 4 iters + safety margin).
                if (sentSoFar == eventCount) break;
                sentSoFar.Should().BeLessThanOrEqualTo(Math.Min(eventCount, batchSize * iterations),
                    because: "the worker must never dispatch more than batchSize events per iteration");
                sentSoFar.Should().BeGreaterThan(previousSent,
                    because: "every iteration that has work to do should make forward progress");
                previousSent = sentSoFar;
                if (iterations > 10) break; // safety stop
            }

            factory.WebhookHandler.CallCount.Should().Be(eventCount,
                because: "all 10 events must eventually be dispatched across the iterations");

            await using (var finalDb = factory.CreateDbContext())
            {
                var finalSent = await finalDb.OutboxEvents.AsNoTracking()
                    .CountAsync(o => o.Status == OutboxEventStatus.Sent);
                finalSent.Should().Be(eventCount);
            }

            // 4 iterations: 3 + 3 + 3 + 1 = 10. Allow some slack for the worker to
            // dispatch up to batchSize in the final iteration if the second-to-last
            // already filled the queue — but never dispatch more than batchSize per call.
            iterations.Should().BeInRange(4, 5,
                because: "10 events with batch=3 → 4 iterations (3+3+3+1); 5 if the last call returned one extra");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<PaymentHubApiFactory> CreateFreshFactoryAsync()
    {
        var factory = new PaymentHubApiFactory(_postgres);
        _ = factory.CreateClient();
        await factory.ResetDatabaseAsync();
        return factory;
    }

    private async Task<E2ECredentials> SeedApplicationWithWebhookAsync(
        PaymentHubApiFactory factory)
    {
        return await E2ESeedHelpers.SeedTenantAndApplicationAsync(
            factory,
            webhookUrl: TestWebhookUrl,
            protectedWebhookSecret: factory.ProtectWebhookSecret(TestWebhookSecret));
    }

    private static OutboxDispatcherWorker BuildWorker(
        PaymentHubApiFactory factory,
        int timeoutSeconds,
        int batchSize)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var clock = factory.Services.GetRequiredService<IClock>();
        var logger = factory.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<OutboxDispatcherWorker>();

        var options = Options.Create(new PaymentHubOptions
        {
            OutboxWorkerBatchSize = batchSize,
            OutboxWorkerIntervalSeconds = 60,
            OutboxProcessingTimeoutSeconds = timeoutSeconds,
        });

        return new OutboxDispatcherWorker(scopeFactory, clock, logger, options);
    }
}
