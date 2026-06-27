using FluentAssertions;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Enums;
using PaymentHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentHub.IntegrationTests.Persistence;

/// <summary>
/// Verifies the persistence lifecycle of <c>OutboxEvent</c>: starting in
/// <c>Pending</c>, transitioning to <c>Processing</c>, then to <c>Sent</c>,
/// plus the safe-retry path that uses <see cref="OutboxEvent.MarkRetryWithCategory"/>
/// (the same path used by <c>OutboxDispatcherWorker</c> after Slice 7-A.7).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxPersistenceTests
{
    private readonly IntegrationTestFactory _factory;

    public OutboxPersistenceTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
        _factory.ResetDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task OutboxEvent_ShouldPersistPendingProcessingAndSentStates()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme Outbox",
            slug: "acme-outbox-1it");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "App Outbox 1IT");

        var outboxEventId = Guid.NewGuid();
        var enqueued = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.status.changed",
            payload: new { paymentId = Guid.NewGuid(), status = "Approved" },
            id: outboxEventId);

        enqueued.Status.Should().Be(OutboxEventStatus.Pending);
        enqueued.RetryCount.Should().Be(0);
        enqueued.SentAt.Should().BeNull();

        // Reload to confirm the row hit the database in the expected shape.
        using var scope = _factory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxEventStore>();

        var reloaded = await repo.GetByIdAsync(outboxEventId, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(OutboxEventStatus.Pending);
        reloaded.PayloadJson.Should().Contain("paymentId");

        // Transition Pending -> Processing -> Sent, persisting each transition
        // through the IOutboxEventStore abstraction (mirrors the worker path).
        reloaded.MarkProcessing();
        await store.SaveAsync(reloaded, CancellationToken.None);

        var processingReloaded = await repo.GetByIdAsync(outboxEventId, CancellationToken.None);
        processingReloaded!.Status.Should().Be(OutboxEventStatus.Processing);

        processingReloaded.MarkSent();
        await store.SaveAsync(processingReloaded, CancellationToken.None);

        var sentReloaded = await repo.GetByIdAsync(outboxEventId, CancellationToken.None);
        sentReloaded.Should().NotBeNull();
        sentReloaded!.Status.Should().Be(OutboxEventStatus.Sent);
        sentReloaded.SentAt.Should().NotBeNull();
        sentReloaded.NextRetryAt.Should().BeNull();
        sentReloaded.LastError.Should().BeNull();
    }

    [Fact]
    public async Task OutboxEvent_SafeRetry_ShouldPersistCategoryWithoutExceptionMessage()
    {
        var tenant = await _factory.SeedTenantAsync(
            id: Guid.NewGuid(),
            name: "Acme Outbox Retry",
            slug: "acme-outbox-retry-1it");
        var application = await _factory.SeedApplicationClientAsync(
            tenantId: tenant.Id,
            id: Guid.NewGuid(),
            name: "App Outbox Retry 1IT");

        var outboxEventId = Guid.NewGuid();
        var enqueued = await _factory.EnqueueOutboxAsync(
            tenantId: tenant.Id,
            applicationId: application.Id,
            eventType: "payment.status.changed",
            payload: new { paymentId = Guid.NewGuid() },
            id: outboxEventId);

        enqueued.Status.Should().Be(OutboxEventStatus.Pending);

        // Simulate a transient dispatch failure by marking retry with the
        // safe category (no ex.Message persistence).
        var nextRetryAt = DateTime.UtcNow.AddMinutes(5);
        enqueued.MarkRetryWithCategory(WebhookDispatcherCategory.NetworkError, nextRetryAt);

        using var scope = _factory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxEventStore>();
        await store.SaveAsync(enqueued, CancellationToken.None);

        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var reloaded = await repo.GetByIdAsync(outboxEventId, CancellationToken.None);

        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(OutboxEventStatus.Pending,
            because: "a retry keeps the event in Pending so the dispatcher picks it up again");
        reloaded.RetryCount.Should().Be(1);
        reloaded.NextRetryAt.Should().NotBeNull();
        reloaded.NextRetryAt!.Value.Should().BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
        reloaded.LastError.Should().Be(WebhookDispatcherCategory.NetworkError.ToString(),
            because: "the safe retry path must persist only the category name, never an exception message");
    }
}
