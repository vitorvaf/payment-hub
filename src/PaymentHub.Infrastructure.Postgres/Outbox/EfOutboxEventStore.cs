using Microsoft.EntityFrameworkCore;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Infrastructure.Postgres.Outbox;

/// <summary>
/// EF Core-backed <see cref="IOutboxEventStore"/>. Updates the supplied entity through the
/// shared <c>PaymentHubDbContext</c> so change tracking and the unit-of-work boundaries
/// managed by the surrounding scope remain intact.
/// </summary>
public sealed class EfOutboxEventStore : IOutboxEventStore
{
    private readonly PaymentHubDbContext _db;

    public EfOutboxEventStore(PaymentHubDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        if (outboxEvent is null) throw new ArgumentNullException(nameof(outboxEvent));

        // Attach the entity when the caller hands us a detached instance (e.g. worker path that
        // loaded events from the repository earlier in the same scope). If the entity is already
        // tracked, EF Core will reuse the tracked instance and we just call SaveChangesAsync.
        if (_db.Entry(outboxEvent).State == EntityState.Detached)
        {
            _db.OutboxEvents.Update(outboxEvent);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}