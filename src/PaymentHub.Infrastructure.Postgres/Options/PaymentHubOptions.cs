namespace PaymentHub.Infrastructure.Postgres.Options;

public sealed class PaymentHubOptions
{
    public const string SectionName = "PaymentHub";

    public string DefaultProvider { get; set; } = "Fake";
    public string ApiKeyHashSecret { get; set; } = string.Empty;
    public string CredentialEncryptionKey { get; set; } = string.Empty;
    public string WebhookSecretEncryptionKey { get; set; } = string.Empty;
    public int WebhookWorkerBatchSize { get; set; } = 25;
    public int OutboxWorkerBatchSize { get; set; } = 50;
    public int WebhookWorkerIntervalSeconds { get; set; } = 5;
    public int OutboxWorkerIntervalSeconds { get; set; } = 2;
    public int WebhookHttpTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Slice 7-M1: how long an <c>OutboxEvent</c> may sit in <c>Processing</c> before the
    /// orphan sweep (<c>SweepOrphanedProcessingAsync</c>) re-enqueues it to <c>Pending</c>.
    /// Default 900s (15 minutes) — generous enough to absorb the production
    /// <c>WebhookHttpTimeoutSeconds</c> (10s) plus retries, while still recovering a
    /// crashed worker within a sensible SLA. Tune downward in environments with tighter
    /// delivery targets; tune upward if legitimate dispatches take longer than 15 minutes.
    /// </summary>
    public int OutboxProcessingTimeoutSeconds { get; set; } = 900;
}
