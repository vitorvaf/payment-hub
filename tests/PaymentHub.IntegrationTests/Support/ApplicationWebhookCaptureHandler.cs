using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace PaymentHub.IntegrationTests.Support;

/// <summary>
/// In-memory HTTP handler that captures every call to the named
/// <c>application-webhook</c> <see cref="HttpClient"/>. Replaces the real
/// <see cref="PaymentHub.Infrastructure.Postgres.Webhooks.HttpApplicationWebhookDispatcher"/>
/// outbound transport so end-to-end tests can assert that an outbound
/// <c>OutboxEvent</c> would have been dispatched.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher is exercised manually from inside the test scope because
/// the production <c>OutboxDispatcherWorker</c> is not hosted by
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// </para>
/// <para>
/// Default behaviour: returns <c>204 No Content</c>. Tests that need to exercise
/// the worker's failure paths (Slice 7-IT — HttpFailure, RateLimited, etc.) push
/// <see cref="ProgrammedResponse"/> entries via <see cref="EnqueueResponse"/>; when
/// the queue is empty, the handler falls back to <c>204</c> so the existing Slice
/// 3-IT assertions (<c>Captured.Should().BeEmpty()</c>) keep passing without setup.
/// </para>
/// </remarks>
public sealed class ApplicationWebhookCaptureHandler : HttpMessageHandler
{
    /// <summary>
    /// One captured outbound POST. Includes the full <c>X-PaymentHub-*</c> header
    /// surface the production dispatcher sends so tests can assert the contract
    /// end-to-end (event id, event type, timestamp, HMAC).
    /// </summary>
    public sealed record CapturedRequest(
        string Method,
        string Url,
        string? SignatureHeader,
        string? TimestampHeader,
        string? EventIdHeader,
        string? EventTypeHeader,
        string Body);

    /// <summary>
    /// Programmable response for the next captured request. Used by tests to
    /// simulate consumer-side failures (5xx, 4xx, network errors) without ever
    /// leaving the test process.
    /// </summary>
    public sealed record ProgrammedResponse(HttpStatusCode StatusCode, string? ReasonPhrase = null);

    private readonly ConcurrentQueue<CapturedRequest> _captured = new();
    private readonly ConcurrentQueue<ProgrammedResponse> _responses = new();
    private CapturedRequest? _last;

    /// <summary>
    /// All captured requests in arrival order.
    /// </summary>
    public IReadOnlyCollection<CapturedRequest> Captured => _captured.ToArray();

    /// <summary>
    /// Most recent captured request (null when no calls have been made yet).
    /// </summary>
    public CapturedRequest? Last => _last;

    /// <summary>
    /// Number of requests the fake transport has received. Convenience for tests
    /// that only want to assert "was it called at all" or "was it called N times".
    /// </summary>
    public int CallCount => _captured.Count;

    /// <summary>
    /// Enqueues a response to be returned by the next request the handler receives.
    /// When the queue is empty, the handler falls back to <c>204 No Content</c>.
    /// </summary>
    public void EnqueueResponse(HttpStatusCode statusCode, string? reasonPhrase = null)
    {
        _responses.Enqueue(new ProgrammedResponse(statusCode, reasonPhrase));
    }

    /// <summary>
    /// Resets both the captured-request log and the response queue. Tests that
    /// reuse a single factory across cases should call this between cases; the
    /// slice 7-IT tests opt for fresh factories, but the helper is here for the
    /// cases that don't.
    /// </summary>
    public void Reset()
    {
        // Drain both queues atomically (best-effort — single thread per test).
        while (_responses.TryDequeue(out _)) { }
        while (_captured.TryDequeue(out _)) { }
        _last = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        var captured = new CapturedRequest(
            Method: request.Method.Method,
            Url: request.RequestUri?.ToString() ?? string.Empty,
            SignatureHeader: ReadHeader(request, "X-PaymentHub-Signature"),
            TimestampHeader: ReadHeader(request, "X-PaymentHub-Timestamp"),
            EventIdHeader: ReadHeader(request, "X-PaymentHub-Event-Id"),
            EventTypeHeader: ReadHeader(request, "X-PaymentHub-Event-Type"),
            Body: body);

        _captured.Enqueue(captured);
        _last = captured;

        // Pop a programmed response if any, otherwise default to 204.
        var statusCode = HttpStatusCode.NoContent;
        string? reason = null;
        if (_responses.TryDequeue(out var programmed))
        {
            statusCode = programmed.StatusCode;
            reason = programmed.ReasonPhrase;
        }

        var response = new HttpResponseMessage(statusCode);
        if (!string.IsNullOrEmpty(reason))
        {
            response.ReasonPhrase = reason;
        }
        return Task.FromResult(response);
    }

    /// <summary>
    /// Reads the named header (case-insensitive). Returns null when the header is
    /// absent on both the request headers and the content headers. Multi-value
    /// headers are joined with a comma to mirror how <see cref="System.Net.Http.Headers.HttpHeaders"/>
    /// naturalise the values downstream.
    /// </summary>
    private static string? ReadHeader(HttpRequestMessage request, string headerName)
    {
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            return string.Join(",", values);
        }
        if (request.Content is not null &&
            request.Content.Headers.TryGetValues(headerName, out var contentValues))
        {
            return string.Join(",", contentValues);
        }
        return null;
    }
}

/// <summary>
/// Pure helpers that recompute / verify the HMAC-SHA256 signature the
/// <c>HttpApplicationWebhookDispatcher</c> emits on the
/// <c>X-PaymentHub-Signature</c> header. The contract (per
/// <c>docs/specs/007-inbox-outbox-workers.md</c> and ADR-0010) is:
/// <c>sha256_hex_lowercase(secret, "{timestamp}.{rawBody}")</c>.
/// </summary>
public static class InternalWebhookHmac
{
    /// <summary>
    /// Recomputes the signature the dispatcher should have emitted. Returns the
    /// hex-lowercase HMAC-SHA256 over <c>"{timestamp}.{rawBody}"</c>.
    /// </summary>
    public static string Compute(string rawBody, string secret, string timestamp)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        var signedPayload = string.IsNullOrEmpty(timestamp)
            ? rawBody
            : $"{timestamp}.{rawBody}";

        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// True when the supplied <paramref name="signature"/> equals
    /// <see cref="Compute"/> in constant time. Avoids byte-by-byte comparison
    /// in tests because the helper is invoked many times per test class and
    /// the timing leak is irrelevant inside the test process.
    /// </summary>
    public static bool Matches(string rawBody, string secret, string timestamp, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        var expected = Compute(rawBody, secret, timestamp);
        return string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase);
    }
}
