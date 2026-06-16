using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Abstractions.Outbox;

public interface IApplicationWebhookDispatcher
{
    Task DispatchAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
