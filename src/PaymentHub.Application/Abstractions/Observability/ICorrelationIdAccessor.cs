namespace PaymentHub.Application.Abstractions.Observability;

/// <summary>
/// Request-scoped accessor for the <c>CorrelationId</c> resolved by the API
/// middleware (<c>CorrelationIdMiddleware</c>). Slice 9-O1 propagates the
/// value from inbound HTTP requests through handlers, the outbox publisher and
/// the dispatch worker.
///
/// <para>
/// The accessor is intentionally read/write so background workers (which run
/// outside of an HTTP context) can also seed the accessor when they bootstrap
/// a flow (e.g. when a queued webhook event is processed). The middleware is
/// the only component expected to call <see cref="Set"/> in the HTTP path.
/// </para>
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// The active <c>CorrelationId</c> for the current request/flow, or
    /// <c>null</c> when no accessor has been initialised (e.g. background
    /// code without a seeded value). The middleware guarantees a non-null
    /// value for every inbound HTTP request.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Stores the supplied <paramref name="id"/> as the active correlation id.
    /// Implementations should reject null/whitespace and silently ignore it
    /// (the middleware will never call this with an invalid id).
    /// </summary>
    void Set(string id);
}
