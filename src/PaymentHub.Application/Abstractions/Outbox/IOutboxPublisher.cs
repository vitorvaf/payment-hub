using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Abstractions.Outbox;

public interface IOutboxPublisher
{
    Task<Guid> EnqueueAsync<TEvent>(
        Guid tenantId,
        Guid applicationId,
        string eventType,
        TEvent @event,
        CancellationToken cancellationToken);

    Task EnqueueAsync<TEvent>(
        Guid outboxEventId,
        Guid tenantId,
        Guid applicationId,
        string eventType,
        TEvent @event,
        CancellationToken cancellationToken);

    /// <summary>
    /// Slice 9-O1.2: overload that also accepts the correlation id resolved
    /// by <c>CorrelationIdMiddleware</c>. The id is persisted in the new
    /// <c>outbox_events.correlation_id</c> column and propagated by the
    /// dispatcher to the outbound <c>X-Correlation-Id</c> header.
    /// </summary>
    Task<Guid> EnqueueAsync<TEvent>(
        Guid tenantId,
        Guid applicationId,
        string eventType,
        TEvent @event,
        string? correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Slice 9-O1.2: deterministic id + correlation id overload used by
    /// callers that already minted the <see cref="OutboxEvent"/> id (e.g.
    /// the checkout handler, which threads the id through the published
    /// payload).
    /// </summary>
    Task EnqueueAsync<TEvent>(
        Guid outboxEventId,
        Guid tenantId,
        Guid applicationId,
        string eventType,
        TEvent @event,
        string? correlationId,
        CancellationToken cancellationToken);
}

public interface IOutboxRepository
{
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Slice 7-M1: atomically claims up to <paramref name="batchSize"/> dispatchable
    /// <c>Pending</c> rows and returns them already transitioned to <c>Processing</c>.
    ///
    /// <para>
    /// Implementations MUST perform the SELECT + UPDATE inside a single Postgres
    /// transaction with <c>FOR UPDATE SKIP LOCKED</c> so concurrent worker instances
    /// never receive the same row. The returned entities carry <c>Status = Processing</c>
    /// and <c>ProcessingStartedAt = <paramref name="now"/></c>, ready to be dispatched
    /// without an extra <c>MarkProcessing</c> round-trip.
    /// </para>
    /// <para>
    /// <paramref name="now"/> MUST be sourced from <c>IClock.UtcNow</c> by the caller so
    /// the persisted <c>processing_started_at</c> matches what the worker observes. This
    /// keeps the orphan-sweep TTL deterministic in tests.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> ClaimPendingForDispatchAsync(int batchSize, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Slice 7-M1: re-enqueues <c>Processing</c> rows whose <c>processing_started_at</c> is
    /// older than <paramref name="cutoff"/> back to <c>Pending</c> so the next dispatch
    /// iteration can re-claim them. Returns the number of rows recovered.
    ///
    /// <para>
    /// The sweep is safe to call from every worker iteration: it only touches rows that
    /// are still in <c>Processing</c> (an transient state by contract), and it persists
    /// only the safe <see cref="PaymentHub.Domain.Enums.WebhookDispatcherCategory.ProcessingOrphaned"/>
    /// category to <c>last_error</c>. Terminal rows (<c>Sent</c>, <c>Failed</c>) are never
    /// re-opened.
    /// </para>
    /// </summary>
    Task<int> SweepOrphanedProcessingAsync(DateTime cutoff, CancellationToken cancellationToken);

    Task<OutboxEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
