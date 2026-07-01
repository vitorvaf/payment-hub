using FluentAssertions;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Enums;
using PaymentHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentHub.IntegrationTests.Persistence;

/// <summary>
/// Verifies the dispatcher-facing claim path on
/// <see cref="IOutboxRepository.ClaimPendingForDispatchAsync"/>:
/// only <c>Pending</c> events with a <c>NextRetryAt</c> in the past (or
/// null) are returned. <c>Sent</c>, <c>Failed</c> and <c>Processing</c>
/// events are excluded. After Slice 7-M1 the claim also flips the
/// surviving rows to <c>Processing</c> in the same transaction; this
/// test asserts that the returned rows have already been transitioned
/// (status=<c>Processing</c>, <c>ProcessingStartedAt</c> non-null).
///
/// Orphan sweep (Processing past TTL) is exercised separately in
/// <c>OutboxProcessingSweepTests</c> (Slice 7-M1.6).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxPendingQueryTests
{
    private readonly IntegrationTestFactory _factory;

    public OutboxPendingQueryTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
        _factory.ResetDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task OutboxRepository_ShouldReturnOnlyDispatchablePendingEvents()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme Pending Query",
            slug: "acme-pending-1it");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "App Pending 1IT");

        var dueNow = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.due-now",
            payload: new { which = "due-now" });

        var dueInPast = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.due-in-past",
            payload: new { which = "due-in-past" });

        var dueInFuture = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.due-in-future",
            payload: new { which = "due-in-future" });

        var processing = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.processing",
            payload: new { which = "processing" });

        var sent = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.sent",
            payload: new { which = "sent" });

        var failed = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.failed",
            payload: new { which = "failed" });

        // Use the IOutboxEventStore + entity transition methods to vary the
        // persisted state of each event. This mirrors how OutboxDispatcherWorker
        // mutates events between iterations.
        using (var scope = _factory.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IOutboxEventStore>();
            var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

            var dueInPastEntity = (await repo.GetByIdAsync(dueInPast.Id, CancellationToken.None))!;
            dueInPastEntity.MarkRetryWithCategory(
                WebhookDispatcherCategory.HttpFailure,
                DateTime.UtcNow.AddMinutes(-5));
            await store.SaveAsync(dueInPastEntity, CancellationToken.None);

            var dueInFutureEntity = (await repo.GetByIdAsync(dueInFuture.Id, CancellationToken.None))!;
            dueInFutureEntity.MarkRetryWithCategory(
                WebhookDispatcherCategory.NetworkError,
                DateTime.UtcNow.AddMinutes(15));
            await store.SaveAsync(dueInFutureEntity, CancellationToken.None);

            var processingEntity = (await repo.GetByIdAsync(processing.Id, CancellationToken.None))!;
            processingEntity.MarkProcessing();
            await store.SaveAsync(processingEntity, CancellationToken.None);

            var sentEntity = (await repo.GetByIdAsync(sent.Id, CancellationToken.None))!;
            sentEntity.MarkSent();
            await store.SaveAsync(sentEntity, CancellationToken.None);

            var failedEntity = (await repo.GetByIdAsync(failed.Id, CancellationToken.None))!;
            failedEntity.MarkFailedWithStatus(WebhookDispatcherCategory.HttpFailure, 502);
            await store.SaveAsync(failedEntity, CancellationToken.None);
        }

        using var queryScope = _factory.CreateScope();
        var pendingRepo = queryScope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var now = DateTime.UtcNow;
        var pending = await pendingRepo.ClaimPendingForDispatchAsync(50, now, CancellationToken.None);

        pending.Should().HaveCount(2,
            because: "only Pending events whose NextRetryAt is null or in the past are dispatchable");

        var pendingIds = pending.Select(o => o.Id).ToHashSet();
        pendingIds.Should().Contain(dueNow.Id);
        pendingIds.Should().Contain(dueInPast.Id);
        pendingIds.Should().NotContain(dueInFuture.Id,
            because: "NextRetryAt is in the future");
        pendingIds.Should().NotContain(processing.Id,
            because: "the query only returns Pending; orphan Processing is handled by the sweep");
        pendingIds.Should().NotContain(sent.Id);
        pendingIds.Should().NotContain(failed.Id);

        // Results should be ordered by CreatedAt ascending.
        pending[0].Id.Should().Be(dueNow.Id,
            because: "dueNow was enqueued before dueInPast, and the query orders by CreatedAt");

        // Slice 7-M1: the claim flips the surviving rows to `Processing` in the same
        // transaction, so the returned entities must already reflect that state.
        pending.Should().OnlyContain(o => o.Status == OutboxEventStatus.Processing,
            because: "ClaimPendingForDispatchAsync transitions the row atomically");
        pending.Should().OnlyContain(o => o.ProcessingStartedAt != null,
            because: "the worker reads ProcessingStartedAt to enforce the orphan TTL");
    }
}
