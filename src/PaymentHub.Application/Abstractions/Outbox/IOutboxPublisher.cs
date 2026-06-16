using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Abstractions.Outbox;

public interface IOutboxPublisher
{
    Task EnqueueAsync<TEvent>(
        Guid tenantId,
        Guid applicationId,
        string eventType,
        TEvent @event,
        CancellationToken cancellationToken);
}

public interface IOutboxRepository
{
    Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboxEvent>> GetPendingForDispatchAsync(int maxItems, CancellationToken cancellationToken);
    Task<OutboxEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
