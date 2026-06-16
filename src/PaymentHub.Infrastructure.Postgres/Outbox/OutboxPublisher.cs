using System.Text.Json;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Infrastructure.Postgres.Outbox;

public sealed class OutboxPublisher : IOutboxPublisher
{
    private readonly Application.Abstractions.Outbox.IOutboxRepository _repository;

    public OutboxPublisher(Application.Abstractions.Outbox.IOutboxRepository repository)
    {
        _repository = repository;
    }

    public async Task EnqueueAsync<TEvent>(
        Guid tenantId,
        Guid applicationId,
        string eventType,
        TEvent @event,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(@event);
        var outbox = new OutboxEvent(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            eventType,
            payload);
        await _repository.AddAsync(outbox, cancellationToken);
    }
}
