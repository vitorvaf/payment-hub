using Microsoft.EntityFrameworkCore;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Infrastructure.Postgres;

public class PaymentHubDbContext : DbContext
{
    public PaymentHubDbContext(DbContextOptions<PaymentHubDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApplicationClient> ApplicationClients => Set<ApplicationClient>();
    public DbSet<ProviderAccount> ProviderAccounts => Set<ProviderAccount>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentHubDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
