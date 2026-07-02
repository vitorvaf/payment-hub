namespace PaymentHub.Worker;

/// <summary>
/// No-op <see cref="PaymentHub.Application.Abstractions.Observability.ICorrelationIdAccessor"/>
/// implementation used in the Worker host. The worker never serves inbound
/// HTTP requests, so the accessor returns <c>null</c> until a background flow
/// (e.g. an inbox processor) seeds it with the value pulled from a persisted
/// <c>webhook_events.correlation_id</c>.
/// </summary>
public sealed class NullCorrelationIdAccessor : PaymentHub.Application.Abstractions.Observability.ICorrelationIdAccessor
{
    public string? CorrelationId => null;

    public void Set(string id)
    {
        // Intentional no-op. The worker seed path goes through a dedicated
        // async-friendly overload (out of scope for the initial slice 9-O1.1).
    }
}
