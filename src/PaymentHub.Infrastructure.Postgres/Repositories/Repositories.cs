using Microsoft.EntityFrameworkCore;
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

    public async Task<IReadOnlyList<OutboxEvent>> GetPendingForDispatchAsync(int maxItems, CancellationToken cancellationToken)
        => await _db.OutboxEvents
            .Where(o => o.Status == Domain.Enums.OutboxEventStatus.Pending
                        && (o.NextRetryAt == null || o.NextRetryAt <= DateTime.UtcNow))
            .OrderBy(o => o.CreatedAt)
            .Take(maxItems)
            .ToListAsync(cancellationToken);

    public Task<OutboxEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.OutboxEvents.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task UpdateAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
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
