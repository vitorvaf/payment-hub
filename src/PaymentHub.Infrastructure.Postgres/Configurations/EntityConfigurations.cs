using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;

namespace PaymentHub.Infrastructure.Postgres.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.Status);
    }
}

public class ApplicationClientConfiguration : IEntityTypeConfiguration<ApplicationClient>
{
    public void Configure(EntityTypeBuilder<ApplicationClient> builder)
    {
        builder.ToTable("application_clients");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(a => a.WebhookUrl).HasColumnName("webhook_url").HasMaxLength(2000);
        builder.Property(a => a.WebhookSecret).HasColumnName("webhook_secret").HasMaxLength(500);
        builder.Property(a => a.DefaultProvider).HasColumnName("default_provider").HasConversion<string?>().HasMaxLength(32);
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.Name }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Status });
    }
}

public class ProviderAccountConfiguration : IEntityTypeConfiguration<ProviderAccount>
{
    public void Configure(EntityTypeBuilder<ProviderAccount> builder)
    {
        builder.ToTable("provider_accounts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id");
        builder.Property(p => p.ApplicationId).HasColumnName("application_id");
        builder.Property(p => p.ProviderCode).HasColumnName("provider_code").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Environment).HasColumnName("environment").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.EncryptedCredentials).HasColumnName("encrypted_credentials").HasColumnType("text").IsRequired();
        builder.Property(p => p.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(p => p.Active).HasColumnName("active").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
        // Slice 2-C — webhook management (non-sensitive configuration).
        //
        // ANTI-REGRESSION (Slice 3-IT Rule 1, mirrored here). The events
        // JSON travels only inside `webhook_events` and the application
        // serialises it with `JsonSerializer.Serialize`. Postgres `jsonb`
        // normalises whitespace on insert (single space after each `:` and
        // `,`), which silently mutates the byte shape of the column. We
        // do NOT need GIN-indexable querying on this JSON, so `text` is
        // the correct type — it preserves the inserted bytes verbatim,
        // in line with the Slice 3-IT decision that any opaque JSON
        // blob that ends up being parsed back by the application
        // should stay as `text`. See
        // `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md`
        // (Anti-Regression Notes, Rule 1).
        //
        // NOTE: `webhookSecret` is NEVER stored in its own column; it
        // travels only inside `encrypted_credentials` as JSON. The four
        // columns below hold the merchant-facing target (callback URL),
        // the events the merchant wants to receive, the timestamp of
        // the last config edit, and the status of the upstream
        // registration attempt. They are safe to return via API.
        builder.Property(p => p.WebhookCallbackUrl).HasColumnName("webhook_callback_url").HasMaxLength(2000);
        builder.Property(p => p.WebhookEvents).HasColumnName("webhook_events").HasColumnType("text");
        builder.Property(p => p.WebhookConfiguredAt).HasColumnName("webhook_configured_at");
        builder.Property(p => p.WebhookRemoteStatus).HasColumnName("webhook_remote_status").HasConversion<string?>().HasMaxLength(32);
        builder.HasIndex(p => new { p.TenantId, p.ApplicationId, p.ProviderCode, p.Environment });
    }
}

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasColumnName("id");
        builder.Property(k => k.TenantId).HasColumnName("tenant_id");
        builder.Property(k => k.ApplicationId).HasColumnName("application_id");
        builder.Property(k => k.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(k => k.KeyHash).HasColumnName("key_hash").HasMaxLength(500).IsRequired();
        builder.Property(k => k.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(32).IsRequired();
        builder.Property(k => k.Active).HasColumnName("active").IsRequired();
        builder.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(k => k.RevokedAt).HasColumnName("revoked_at");
        builder.Property(k => k.LastUsedAt).HasColumnName("last_used_at");
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => new { k.TenantId, k.ApplicationId });
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id");
        builder.Property(p => p.ApplicationId).HasColumnName("application_id");
        builder.Property(p => p.ExternalReference).HasColumnName("external_reference").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Amount).HasColumnName("amount_in_cents").IsRequired().HasConversion(new MoneyToLongConverter());
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.SelectedProvider).HasColumnName("selected_provider").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.ProviderPaymentId).HasColumnName("provider_payment_id").HasMaxLength(200);
        builder.Property(p => p.CheckoutUrl).HasColumnName("checkout_url").HasMaxLength(2000);
        builder.Property(p => p.CustomerEmail).HasColumnName("customer_email").HasMaxLength(200);
        builder.Property(p => p.CustomerName).HasColumnName("customer_name").HasMaxLength(200);
        builder.Property(p => p.SuccessUrl).HasColumnName("success_url").HasMaxLength(2000);
        builder.Property(p => p.CancelUrl).HasColumnName("cancel_url").HasMaxLength(2000);
        builder.Property(p => p.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(p => p.ProcessedAt).HasColumnName("processed_at");
        builder.HasMany(p => p.Attempts).WithOne().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.TenantId, p.ApplicationId });
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.ApplicationId, p.ExternalReference });
        builder.HasIndex(p => new { p.SelectedProvider, p.ProviderPaymentId });
        builder.HasIndex(p => p.CreatedAt);
    }
}

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.PaymentId).HasColumnName("payment_id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.ApplicationId).HasColumnName("application_id");
        builder.Property(a => a.ProviderCode).HasColumnName("provider_code").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.ProviderPaymentId).HasColumnName("provider_payment_id").HasMaxLength(200);
        builder.Property(a => a.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.PaymentId });
    }
}

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");
        builder.Property(w => w.TenantId).HasColumnName("tenant_id");
        builder.Property(w => w.ApplicationId).HasColumnName("application_id");
        builder.Property(w => w.ProviderCode).HasColumnName("provider_code").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(w => w.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(200);
        builder.Property(w => w.EventType).HasColumnName("event_type").HasMaxLength(80).IsRequired();
        // ⚠️ ANTI-REGRESSION (Slice 3-IT Rule 1, BLOCKER for Phase 7-IT).
        // raw_payload MUST remain `text` for byte-exact preservation of the
        // raw HTTP body. Postgres `jsonb` parses and normalises the JSON on
        // insert, mutating whitespace (single space after every colon and
        // comma) and breaking HMAC verification over the raw body. The
        // application treats the payload as opaque (passed straight to the
        // provider adapter for verification + normalization), so `text` is
        // the correct storage type. Migration `20260629205545_ChangeRawPayloadToText`
        // downgrades the original `jsonb` column. **DO NOT** change this back
        // to `jsonb` — the E2E test `ProviderWebhook_ValidSignature_UpdatesPaymentAndEnqueuesOutbox`
        // will fail with `AbacatePay webhook signature invalid (SignatureMismatch)`.
        // See `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md`
        // (Anti-Regression Notes, Rule 1) and `feature_list.md` entry
        // `PH-PROVIDER-WEBHOOK-RAWPAYLOAD-TEXT`.
        builder.Property(w => w.RawPayloadJson).HasColumnName("raw_payload").HasColumnType("text").IsRequired();
        builder.Property(w => w.Signature).HasColumnName("signature").HasMaxLength(500);
        builder.Property(w => w.ProcessingStatus).HasColumnName("processing_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(w => w.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(w => w.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(w => w.ProcessedAt).HasColumnName("processed_at");
        builder.Property(w => w.NextRetryAt).HasColumnName("next_retry_at");
        builder.Property(w => w.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at").IsRequired();
        // Slice 9-O1.2: non-sensitive correlation id resolved by
        // CorrelationIdMiddleware and propagated through the inbox → outbox
        // → dispatcher flow. MaxLength mirrors the bounded
        // CorrelationIdGenerator validation window so the DB never receives
        // a value the API rejected.
        builder.Property(w => w.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
        builder.HasIndex(w => new { w.ProviderCode, w.ProviderEventId })
            .IsUnique()
            .HasFilter("provider_event_id IS NOT NULL");
        builder.HasIndex(w => new { w.ProcessingStatus, w.NextRetryAt });
        builder.HasIndex(w => w.ReceivedAt);
    }
}

public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id");
        builder.Property(o => o.ApplicationId).HasColumnName("application_id");
        builder.Property(o => o.EventType).HasColumnName("event_type").HasMaxLength(80).IsRequired();
        builder.Property(o => o.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(o => o.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(o => o.SentAt).HasColumnName("sent_at");
        builder.Property(o => o.NextRetryAt).HasColumnName("next_retry_at");
        // Slice 7-M1 — multi-instance support. `processing_started_at` is the timestamp the
        // row was atomically flipped to `Processing` by `ClaimPendingForDispatchAsync`. The
        // orphan sweep uses it to detect rows whose worker died mid-dispatch. Nullable because
        // every non-`Processing` state (Pending/Sent/Failed) must have it cleared by the
        // entity transition methods. See `OutboxEvent.MarkProcessing`/`MarkSent`/etc.
        builder.Property(o => o.ProcessingStartedAt).HasColumnName("processing_started_at");
        // Slice 9-O1.2: correlation id propagated from the originating HTTP
        // request. The dispatcher reads it from this column and echoes it on
        // the outbound X-Correlation-Id header so consumers can stitch logs
        // across the two systems. Nullable: legacy rows and background seeds
        // carry null. MaxLength mirrors the bounded
        // CorrelationIdGenerator validation window.
        builder.Property(o => o.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();
        // Claim index covers `ClaimPendingForDispatchAsync`: filters on `status = 'Pending'`
        // plus `next_retry_at IS NULL OR next_retry_at <= @now`, ordered by `created_at`.
        // The previous `(status, next_retry_at)` index is replaced by `(status, next_retry_at, created_at)`
        // so the ORDER BY can be served from the index without a sort step.
        builder.HasIndex(o => new { o.Status, o.NextRetryAt, o.CreatedAt });
        // Sweep index covers `SweepOrphanedProcessingAsync`: filters on `status = 'Processing'`
        // plus `processing_started_at < @cutoff`. Partial index keeps it small in steady state
        // (only `Processing` rows are present in the index).
        builder.HasIndex(o => new { o.Status, o.ProcessingStartedAt })
            .HasFilter("status = 'Processing'");
        builder.HasIndex(o => o.CreatedAt);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.TenantId).HasColumnName("tenant_id");
        builder.Property(l => l.ApplicationId).HasColumnName("application_id");
        builder.Property(l => l.Actor).HasColumnName("actor").HasMaxLength(200).IsRequired();
        builder.Property(l => l.Action).HasColumnName("action").HasMaxLength(80).IsRequired();
        builder.Property(l => l.Entity).HasColumnName("entity").HasMaxLength(120);
        builder.Property(l => l.EntityId).HasColumnName("entity_id").HasMaxLength(120);
        builder.Property(l => l.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(l => new { l.TenantId, l.CreatedAt });
    }
}

public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasColumnName("id");
        builder.Property(k => k.TenantId).HasColumnName("tenant_id");
        builder.Property(k => k.ApplicationId).HasColumnName("application_id");
        builder.Property(k => k.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        builder.Property(k => k.RequestHash).HasColumnName("request_hash").HasMaxLength(128).IsRequired();
        builder.Property(k => k.PaymentId).HasColumnName("payment_id");
        builder.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(k => new { k.TenantId, k.ApplicationId, k.Key }).IsUnique();
    }
}
