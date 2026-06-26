using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Abstractions.Outbox;

/// <summary>
/// Persistence boundary for <see cref="OutboxEvent"/> mutations triggered by the Outbox worker
/// (mark as Processing, Sent, Retry or Failed). Lives next to <see cref="IOutboxRepository"/> so
/// the worker does not need to take a direct dependency on <c>PaymentHubDbContext</c> for the
/// common "update an outbox row" case.
///
/// Implementations are expected to be idempotent per call: calling <see cref="SaveAsync"/> with
/// the same outbox event more than once must remain safe (concurrency control is the caller's
/// responsibility via the outbox repository's selection rules).
/// </summary>
public interface IOutboxEventStore
{
    Task SaveAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}