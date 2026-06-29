namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Categorized failure mode for AbacatePay HTTP calls. <see cref="IsTransient"/>
/// drives retry policy at the dispatcher layer; the adapter never logs the
/// raw response body, API key, or Authorization header.
/// </summary>
public enum AbacatePayErrorCategory
{
    /// <summary>HTTP 400 — malformed payload. Not retriable.</summary>
    BadRequest = 1,

    /// <summary>HTTP 401 or 403 — invalid/expired API key or scope. Not retriable.</summary>
    Unauthorized = 2,

    /// <summary>HTTP 404 — provider payment id not found. Not retriable.</summary>
    NotFound = 3,

    /// <summary>HTTP 429 — rate limited. Retriable.</summary>
    RateLimited = 4,

    /// <summary>HTTP 5xx — provider outage. Retriable.</summary>
    ServerError = 5,

    /// <summary>DNS / socket / TLS failure. Retriable.</summary>
    Network = 6,

    /// <summary>Request timed out (TaskCanceledException with cancellation by HttpClient). Retriable.</summary>
    Timeout = 7,

    /// <summary>HTTP 2xx with <c>success=false</c> envelope. Not retriable by default.</summary>
    EnvelopeFailure = 8,

    /// <summary>Catch-all for unexpected exceptions. Not retriable by default.</summary>
    Unexpected = 9,

    /// <summary>Simulation endpoint was called while <c>AllowDevModeSimulation=false</c>. Not retriable.</summary>
    SimulationDisabled = 10
}