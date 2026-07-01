using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Infrastructure.Postgres.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly PaymentHubDbContext _db;
    public UnitOfWork(PaymentHubDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}

public class TenantRepository : ITenantRepository
{
    private readonly PaymentHubDbContext _db;
    public TenantRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
        => await _db.Tenants.AddAsync(tenant, cancellationToken);

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        => _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == id, cancellationToken);
}

public class ApplicationClientRepository : IApplicationClientRepository
{
    private readonly PaymentHubDbContext _db;
    public ApplicationClientRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(ApplicationClient client, CancellationToken cancellationToken)
        => await _db.ApplicationClients.AddAsync(client, cancellationToken);

    public Task<ApplicationClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.ApplicationClients.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<ApplicationClient?> GetByTenantAndIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        => _db.ApplicationClients.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, cancellationToken);

    public Task<ApplicationClient?> GetByTenantAndNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
        => _db.ApplicationClients.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Name == name, cancellationToken);

    public Task<bool> ExistsAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        => _db.ApplicationClients.AsNoTracking().AnyAsync(a => a.TenantId == tenantId && a.Id == id, cancellationToken);
}

public class ProviderAccountRepository : IProviderAccountRepository
{
    private readonly PaymentHubDbContext _db;
    public ProviderAccountRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(ProviderAccount account, CancellationToken cancellationToken)
        => await _db.ProviderAccounts.AddAsync(account, cancellationToken);

    public Task<ProviderAccount?> GetDefaultAsync(Guid tenantId, Guid applicationId, ProviderCode code, CancellationToken cancellationToken)
        => _db.ProviderAccounts.FirstOrDefaultAsync(p =>
            p.TenantId == tenantId && p.ApplicationId == applicationId && p.ProviderCode == code && p.IsDefault && p.Active, cancellationToken);

    public Task<ProviderAccount?> GetByCodeAsync(Guid tenantId, Guid applicationId, ProviderCode code, CancellationToken cancellationToken)
        => _db.ProviderAccounts.FirstOrDefaultAsync(p =>
            p.TenantId == tenantId && p.ApplicationId == applicationId && p.ProviderCode == code && p.Active, cancellationToken);

    public Task<ProviderAccount?> GetByIdForTenantAndApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        Guid providerAccountId,
        CancellationToken cancellationToken)
        => _db.ProviderAccounts.FirstOrDefaultAsync(p =>
            p.Id == providerAccountId
            && p.TenantId == tenantId
            && p.ApplicationId == applicationId,
            cancellationToken);

    public Task UpdateAsync(ProviderAccount account, CancellationToken cancellationToken)
    {
        // EF Core change tracker detects mutations on tracked entities;
        // AddAsync followed by mutations correctly issues UPDATE.
        _db.ProviderAccounts.Update(account);
        return Task.CompletedTask;
    }
}

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly PaymentHubDbContext _db;
    public ApiKeyRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken)
        => await _db.ApiKeys.AddAsync(apiKey, cancellationToken);

    public Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken)
        => _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.Active, cancellationToken);
}

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentHubDbContext _db;
    public PaymentRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        await _db.Payments.AddAsync(payment, cancellationToken);
        if (payment.Attempts.Any())
        {
            await _db.PaymentAttempts.AddRangeAsync(payment.Attempts, cancellationToken);
        }
    }

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Payment?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        => _db.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

    public Task<Payment?> GetByProviderPaymentIdAsync(string providerCode, string providerPaymentId, CancellationToken cancellationToken)
        => _db.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.SelectedProvider.ToString() == providerCode && p.ProviderPaymentId == providerPaymentId, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListAsync(Guid tenantId, Guid applicationId, int skip, int take, CancellationToken cancellationToken)
    {
        var query = _db.Payments.AsNoTracking().AsQueryable();
        if (tenantId != Guid.Empty) query = query.Where(p => p.TenantId == tenantId);
        if (applicationId != Guid.Empty) query = query.Where(p => p.ApplicationId == applicationId);
        return await query.OrderByDescending(p => p.CreatedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        _db.Payments.Update(payment);
        return Task.CompletedTask;
    }

    public async Task AddAttemptAsync(PaymentAttempt attempt, CancellationToken cancellationToken)
        => await _db.PaymentAttempts.AddAsync(attempt, cancellationToken);
}

public class WebhookEventRepository : IWebhookEventRepository
{
    private readonly PaymentHubDbContext _db;
    public WebhookEventRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken)
        => await _db.WebhookEvents.AddAsync(webhookEvent, cancellationToken);

    public Task<WebhookEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.WebhookEvents.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<WebhookEvent?> GetByProviderEventIdAsync(string providerCode, string providerEventId, CancellationToken cancellationToken)
        => _db.WebhookEvents.FirstOrDefaultAsync(w =>
            w.ProviderCode.ToString() == providerCode && w.ProviderEventId == providerEventId, cancellationToken);

    public async Task<IReadOnlyList<WebhookEvent>> GetPendingAsync(int maxItems, CancellationToken cancellationToken)
        => await _db.WebhookEvents
            .Where(w => w.ProcessingStatus == Domain.Enums.WebhookProcessingStatus.Pending
                        && (w.NextRetryAt == null || w.NextRetryAt <= DateTime.UtcNow))
            .OrderBy(w => w.ReceivedAt)
            .Take(maxItems)
            .ToListAsync(cancellationToken);

    public Task UpdateAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        _db.WebhookEvents.Update(webhookEvent);
        return Task.CompletedTask;
    }
}

public class OutboxRepository : Application.Abstractions.Outbox.IOutboxRepository
{
    private readonly PaymentHubDbContext _db;
    public OutboxRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
        => await _db.OutboxEvents.AddAsync(outboxEvent, cancellationToken);

    /// <summary>
    /// Slice 7-M1: atomic claim of dispatchable <c>Pending</c> rows. Runs SELECT ...
    /// FOR UPDATE SKIP LOCKED and UPDATE in a single transaction so two concurrent worker
    /// instances never receive the same row.
    ///
    /// <para>
    /// Implementation notes:
    /// </para>
    /// <list type="bullet">
    /// <item>We drop down to a raw <see cref="NpgsqlConnection"/> (via
    /// <c>Database.GetDbConnection</c>) because EF Core 10 has no first-class LINQ
    /// translation for <c>SKIP LOCKED</c>. ADO.NET gives us full control over the
    /// transaction boundary.</item>
    /// <item><c>next_retry_at</c> semantics: a row is dispatchable when it is NULL
    /// (never retried) or &lt;= <paramref name="now"/> (backoff expired). Rows with a
    /// future <c>next_retry_at</c> are skipped.</item>
    /// <item>After the UPDATE commits, we reload the claimed rows via EF Core so the
    /// caller receives tracked entities with all mapped columns populated. The EF
    /// tracker is unaware of the ADO.NET UPDATE, so we use <c>AsNoTracking</c> on the
    /// reload to avoid stale-cache collisions with the same row in the same DbContext
    /// (the worker re-saves through <c>IOutboxEventStore</c>, which re-attaches).</item>
    /// </list>
    /// </summary>
    public async Task<IReadOnlyList<OutboxEvent>> ClaimPendingForDispatchAsync(
        int batchSize,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0) return Array.Empty<OutboxEvent>();

        // Snapshot the connection so we can drive the transaction ourselves. EF Core
        // owns this DbConnection; we must NOT dispose it.
        var dbConnection = _db.Database.GetDbConnection();
        var connectionWasClosed = dbConnection.State != System.Data.ConnectionState.Open;
        if (connectionWasClosed)
        {
            await dbConnection.OpenAsync(cancellationToken);
        }

        List<Guid> claimedIds;
        await using (var transaction = await dbConnection.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken))
        {
            const string selectSql = @"
SELECT id
FROM outbox_events
WHERE status = 'Pending'
  AND (next_retry_at IS NULL OR next_retry_at <= @now)
ORDER BY created_at
FOR UPDATE SKIP LOCKED
LIMIT @batchSize;";

            await using (var selectCommand = ((NpgsqlConnection)dbConnection).CreateCommand())
            {
                selectCommand.Transaction = (NpgsqlTransaction)transaction;
                selectCommand.CommandText = selectSql;
                selectCommand.Parameters.Add(new NpgsqlParameter<DateTime>("now", NpgsqlDbType.TimestampTz) { TypedValue = now });
                selectCommand.Parameters.Add(new NpgsqlParameter<int>("batchSize", NpgsqlDbType.Integer) { TypedValue = batchSize });

                claimedIds = new List<Guid>(batchSize);
                await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        claimedIds.Add(reader.GetFieldValue<Guid>(0));
                    }
                }
            }

            if (claimedIds.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                if (connectionWasClosed)
                {
                    await dbConnection.CloseAsync();
                }
                return Array.Empty<OutboxEvent>();
            }

            const string updateSql = @"
UPDATE outbox_events
SET status = 'Processing',
    processing_started_at = @now,
    updated_at = @now
WHERE id = ANY(@claimedIds);";

            await using (var updateCommand = ((NpgsqlConnection)dbConnection).CreateCommand())
            {
                updateCommand.Transaction = (NpgsqlTransaction)transaction;
                updateCommand.CommandText = updateSql;
                updateCommand.Parameters.Add(new NpgsqlParameter<DateTime>("now", NpgsqlDbType.TimestampTz) { TypedValue = now });
                updateCommand.Parameters.Add(new NpgsqlParameter<Guid[]>("claimedIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { TypedValue = claimedIds.ToArray() });
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (connectionWasClosed)
        {
            await dbConnection.CloseAsync();
        }

        // Reload through EF Core so the caller receives entities with all mapped columns
        // populated. AsNoTracking avoids stale-cache collisions: the worker re-saves via
        // IOutboxEventStore which re-attaches on its own.
        var reloaded = await _db.OutboxEvents
            .AsNoTracking()
            .Where(o => claimedIds.Contains(o.Id))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return reloaded;
    }

    /// <summary>
    /// Slice 7-M1: orphan sweep. Re-enqueues rows stuck in <c>Processing</c> past the
    /// TTL back to <c>Pending</c>. Uses a single UPDATE ... WHERE so it is atomic and
    /// idempotent. The category persisted to <c>last_error</c> is
    /// <see cref="WebhookDispatcherCategory.ProcessingOrphaned"/>; never the original
    /// exception, URL, body or signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep sets <c>next_retry_at = NULL</c> (not <c>@now</c>) so the row is
    /// immediately dispatchable in the same iteration. If we stamped <c>next_retry_at = now</c>,
    /// the claim's filter <c>(next_retry_at IS NULL OR next_retry_at &lt;= @now)</c> would
    /// require strict &lt;=; with a brand-new <c>now</c> from the claim path (microseconds
    /// later), the comparison would fail and the row would stay in <c>Pending</c> for one
    /// extra tick. Setting it to <c>NULL</c> makes the dispatch deterministic.
    /// </para>
    /// </remarks>
    public async Task<int> SweepOrphanedProcessingAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE outbox_events
SET status = 'Pending',
    retry_count = retry_count + 1,
    last_error = 'ProcessingOrphaned',
    next_retry_at = NULL,
    processing_started_at = NULL,
    updated_at = @now
WHERE status = 'Processing'
  AND processing_started_at IS NOT NULL
  AND processing_started_at < @cutoff;";

        var now = DateTime.UtcNow;
        return await _db.Database.ExecuteSqlRawAsync(sql,
            new object[] {
                new NpgsqlParameter<DateTime>("now", NpgsqlDbType.TimestampTz) { TypedValue = now },
                new NpgsqlParameter<DateTime>("cutoff", NpgsqlDbType.TimestampTz) { TypedValue = cutoff },
            },
            cancellationToken);
    }

    public Task<OutboxEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.OutboxEvents.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task UpdateAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        // EF Core change tracker detects mutations on tracked entities;
        // AddAsync followed by mutations correctly issues UPDATE.
        _db.OutboxEvents.Update(outboxEvent);
        return Task.CompletedTask;
    }
}

public class IdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private readonly PaymentHubDbContext _db;
    public IdempotencyKeyRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(IdempotencyKey key, CancellationToken cancellationToken)
        => await _db.IdempotencyKeys.AddAsync(key, cancellationToken);

    public Task<IdempotencyKey?> FindAsync(Guid tenantId, Guid applicationId, string key, CancellationToken cancellationToken)
        => _db.IdempotencyKeys.FirstOrDefaultAsync(k =>
            k.TenantId == tenantId && k.ApplicationId == applicationId && k.Key == key, cancellationToken);
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly PaymentHubDbContext _db;
    public AuditLogRepository(PaymentHubDbContext db) => _db = db;

    public async Task AddAsync(AuditLog log, CancellationToken cancellationToken)
        => await _db.AuditLogs.AddAsync(log, cancellationToken);
}
