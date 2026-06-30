using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ITenantRepository
{
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
}

public interface IApplicationClientRepository
{
    Task AddAsync(ApplicationClient client, CancellationToken cancellationToken);
    Task<ApplicationClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ApplicationClient?> GetByTenantAndIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<ApplicationClient?> GetByTenantAndNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}

public interface IProviderAccountRepository
{
    Task AddAsync(ProviderAccount account, CancellationToken cancellationToken);
    Task<ProviderAccount?> GetDefaultAsync(Guid tenantId, Guid applicationId, ProviderCode code, CancellationToken cancellationToken);
    Task<ProviderAccount?> GetByCodeAsync(Guid tenantId, Guid applicationId, ProviderCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a specific <c>ProviderAccount</c> by id scoped to the
    /// caller-provided tenant and application, WITHOUT filtering on
    /// <c>Active</c>. Used by the webhook management endpoints (Slice 2-C)
    /// to distinguish <c>404 Not Found</c> (row absent in the caller's
    /// scope) from <c>409 Conflict</c> (row present but marked inactive).
    ///
    /// Returns <c>null</c> when the row does not exist for the given
    /// tenant/application.
    /// </summary>
    Task<ProviderAccount?> GetByIdForTenantAndApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        Guid providerAccountId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generic update entry point — needs no implementation work because
    /// EF Core's <c>DbContext.SaveChangesAsync</c> drives the change
    /// tracker. Provided here so application handlers can rely on a
    /// typed API instead of touching the <c>DbContext</c> directly.
    /// </summary>
    Task UpdateAsync(ProviderAccount account, CancellationToken cancellationToken);
}

public interface IApiKeyRepository
{
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken);
    Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken);
}

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Payment?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Payment?> GetByProviderPaymentIdAsync(string providerCode, string providerPaymentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Payment>> ListAsync(Guid tenantId, Guid applicationId, int skip, int take, CancellationToken cancellationToken);
    Task UpdateAsync(Payment payment, CancellationToken cancellationToken);
    Task AddAttemptAsync(PaymentAttempt attempt, CancellationToken cancellationToken);
}

public interface IWebhookEventRepository
{
    Task AddAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken);
    Task<WebhookEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<WebhookEvent?> GetByProviderEventIdAsync(string providerCode, string providerEventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookEvent>> GetPendingAsync(int maxItems, CancellationToken cancellationToken);
    Task UpdateAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken);
}

public interface IIdempotencyKeyRepository
{
    Task AddAsync(IdempotencyKey key, CancellationToken cancellationToken);
    Task<IdempotencyKey?> FindAsync(Guid tenantId, Guid applicationId, string key, CancellationToken cancellationToken);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken cancellationToken);
}
