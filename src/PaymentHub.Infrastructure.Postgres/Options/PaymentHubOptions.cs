namespace PaymentHub.Infrastructure.Postgres.Options;

public sealed class PaymentHubOptions
{
    public const string SectionName = "PaymentHub";

    public string DefaultProvider { get; set; } = "Fake";
    public string ApiKeyHashSecret { get; set; } = string.Empty;
    public string CredentialEncryptionKey { get; set; } = string.Empty;
    public int WebhookWorkerBatchSize { get; set; } = 25;
    public int OutboxWorkerBatchSize { get; set; } = 50;
    public int WebhookWorkerIntervalSeconds { get; set; } = 5;
    public int OutboxWorkerIntervalSeconds { get; set; } = 2;
    public int WebhookHttpTimeoutSeconds { get; set; } = 10;
}
