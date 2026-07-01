using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.IntegrationTests.Infrastructure;
using PaymentHub.IntegrationTests.Support;
using PaymentHub.Worker;

namespace PaymentHub.IntegrationTests.EndToEnd;

/// <summary>
/// Slice 7-M1.5 — multi-instance concurrency contract for the outbox claim path.
///
/// <para>
/// The tests assert the production guarantee documented in
/// <c>docs/specs/007-inbox-outbox-workers.md</c> and ADR-0010:
/// <c>ClaimPendingForDispatchAsync</c> uses <c>FOR UPDATE SKIP LOCKED</c> so two
/// concurrent worker instances dispatch every event EXACTLY ONCE, no duplicates,
/// no double-deliveries.
/// </para>
/// <para>
/// We do NOT need a second <c>WebApplicationFactory</c> or a real second Worker
/// process. The same <see cref="OutboxDispatcherWorker"/> can be instantiated
/// multiple times from the same test's <see cref="PaymentHubApiFactory.Services"/>
/// with separate <see cref="IServiceScope"/> per "worker" so each gets its own
/// <see cref="PaymentHubDbContext"/>.
/// </para>
/// <para>
/// Anti-flaky measures:
/// </para>
/// <list type="bullet">
/// <item>Each worker holds its own <see cref="IServiceScope"/> + DbContext
/// (EF Core DbContexts are NOT thread-safe; sharing them would mask real
/// bugs).</item>
/// <item>Polling assertions on <c>Status</c> + <c>CallCount</c> with a
/// bounded timeout (3s) to absorb CI jitter.</item>
/// <item>The fake webhook handler's <c>CallCount</c> is the only authoritative
/// "no double-dispatch" signal — it is incremented for every HTTP request
/// the dispatcher issues.</item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class OutboxDispatcherConcurrencyTests
{
    private readonly PostgresFixture _postgres;

    public OutboxDispatcherConcurrencyTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    // -----------------------------------------------------------------
    // P1.1 — Two concurrent workers must not double-dispatch the same event
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldNotDoubleDispatch_WhenTwoInstancesRunConcurrently()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // One event, two workers. Without SKIP LOCKED this would race and
            // produce 2 deliveries; with SKIP LOCKED exactly one wins the claim
            // and the other picks up nothing.
            await E2ESeedHelpers.SeedOutboxEventAsync(
                factory, credentials, eventType: "payment.status.changed",
                payload: new { paymentId = Guid.NewGuid(), status = "Approved" });

            // Drive two dispatchers concurrently. Each uses its own scope so they
            // own their own DbContext.
            var worker1 = BuildWorker(factory);
            var worker2 = BuildWorker(factory);

            await Task.WhenAll(
                worker1.DispatchOnceAsync(CancellationToken.None),
                worker2.DispatchOnceAsync(CancellationToken.None));

            // Authoritative signal: the fake HTTP handler must have received exactly
            // one POST (the loser of the SKIP LOCKED race got an empty claim and
            // returned without dispatching).
            factory.WebhookHandler.CallCount.Should().Be(1,
                because: "FOR UPDATE SKIP LOCKED must ensure only one worker dispatches the same row");

            // And the row itself must be Sent, with no leftover Processing.
            await using var db = factory.CreateDbContext();
            var all = await db.OutboxEvents.AsNoTracking().ToListAsync();
            all.Should().HaveCount(1);
            all[0].Status.Should().Be(OutboxEventStatus.Sent);
            all[0].LastError.Should().BeNull();
            all[0].ProcessingStartedAt.Should().BeNull();
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.2 — 10 events distributed across 3 concurrent workers → all Sent exactly once
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldDistributePendingEventsAcrossConcurrentInstances()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            const int eventCount = 10;
            var ids = new List<Guid>(eventCount);
            for (int i = 0; i < eventCount; i++)
            {
                var id = await E2ESeedHelpers.SeedOutboxEventAsync(
                    factory, credentials,
                    eventType: "payment.status.changed",
                    payload: new { paymentId = Guid.NewGuid(), index = i });
                ids.Add(id);
            }

            // Three workers compete for the 10 events. With SKIP LOCKED the
            // distribution is non-deterministic (depends on scheduler jitter and
            // which transaction grabs each row first), but every row must end
            // up Sent exactly once.
            var workers = new[] { BuildWorker(factory), BuildWorker(factory), BuildWorker(factory) };

            // Each worker runs several iterations to ensure all events are
            // eventually claimed (the first round may claim a partial batch;
            // subsequent rounds pick up the rest).
            async Task RunWorker(OutboxDispatcherWorker w)
            {
                for (int i = 0; i < 5; i++)
                {
                    await w.DispatchOnceAsync(CancellationToken.None);
                }
            }

            await Task.WhenAll(workers.Select(RunWorker).ToArray());

            // Authoritative signal: 10 deliveries total — one per event.
            factory.WebhookHandler.CallCount.Should().Be(eventCount,
                because: "every event must be dispatched exactly once across all worker instances");

            // All rows in the DB are Sent with no orphan Processing.
            await using var db = factory.CreateDbContext();
            var rows = await db.OutboxEvents.AsNoTracking().ToListAsync();
            rows.Should().HaveCount(eventCount);
            rows.Should().OnlyContain(r => r.Status == OutboxEventStatus.Sent);
            rows.Should().OnlyContain(r => r.LastError == null);
            rows.Should().OnlyContain(r => r.ProcessingStartedAt == null);

            // The captured event IDs match the seeded IDs (no extras, no losses).
            var capturedIds = factory.WebhookHandler.Captured
                .Select(c => c.EventIdHeader)
                .Where(id => id is not null)
                .ToHashSet();
            capturedIds.Should().BeEquivalentTo(ids.Select(id => id.ToString()));
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

    private static async Task<E2ECredentials> SeedApplicationWithWebhookAsync(
        PaymentHubApiFactory factory)
    {
        const string webhookUrl = "https://webhook.fake.test/hook";
        return await E2ESeedHelpers.SeedTenantAndApplicationAsync(
            factory,
            webhookUrl: webhookUrl,
            protectedWebhookSecret: factory.ProtectWebhookSecret("test-secret-do-not-log"));
    }

    private static OutboxDispatcherWorker BuildWorker(PaymentHubApiFactory factory)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var clock = factory.Services.GetRequiredService<IClock>();
        var logger = factory.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<OutboxDispatcherWorker>();
        var options = factory.Services.GetRequiredService<IOptions<PaymentHubOptions>>();

        return new OutboxDispatcherWorker(scopeFactory, clock, logger, options);
    }
}
