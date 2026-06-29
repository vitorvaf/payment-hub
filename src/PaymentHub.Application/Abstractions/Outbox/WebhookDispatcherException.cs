using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Abstractions.Outbox;

/// <summary>
/// Typed exception raised by an <see cref="IApplicationWebhookDispatcher"/> when an outbound
/// webhook delivery cannot complete successfully. Carries only the information the
/// <c>OutboxDispatcherWorker</c> needs to populate <c>OutboxEvent.LastError</c> safely — a
/// <see cref="WebhookDispatcherCategory"/> and an optional HTTP status code. The exception
/// <see cref="Exception.Message"/> is intentionally generic so it can be logged without
/// risking leakage of consumer-controlled payloads, query strings or secrets.
/// </summary>
public sealed class WebhookDispatcherException : Exception
{
    public WebhookDispatcherCategory Category { get; }
    public int? StatusCode { get; }

    public WebhookDispatcherException(WebhookDispatcherCategory category, string message)
        : base(message)
    {
        Category = category;
    }

    public WebhookDispatcherException(WebhookDispatcherCategory category, int statusCode, string message)
        : base(message)
    {
        Category = category;
        StatusCode = statusCode;
    }
}