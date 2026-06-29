using System.Collections.Concurrent;
using System.Net;

namespace PaymentHub.IntegrationTests.Support;

/// <summary>
/// In-memory HTTP handler that captures every call to the named
/// <c>application-webhook</c> <see cref="HttpClient"/>. Replaces the real
/// <see cref="PaymentHub.Infrastructure.Postgres.Webhooks.HttpApplicationWebhookDispatcher"/>
/// outbound transport so end-to-end tests can assert that an outbound
/// <c>OutboxEvent</c> would have been dispatched (P2 in the Slice 3-IT plan).
/// </summary>
/// <remarks>
/// The dispatcher is exercised manually from inside the test scope because
/// the production <c>OutboxDispatcherWorker</c> is not hosted by
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// The handler always returns <c>204 No Content</c> so any consumer logic
/// downstream of the HTTP transport is irrelevant for this slice.
/// </remarks>
public sealed class ApplicationWebhookCaptureHandler : HttpMessageHandler
{
    public sealed record CapturedRequest(
        string Method,
        string Url,
        string? SignatureHeader,
        string? TimestampHeader,
        string Body);

    private readonly ConcurrentQueue<CapturedRequest> _captured = new();
    private CapturedRequest? _last;

    /// <summary>
    /// All captured requests in arrival order.
    /// </summary>
    public IReadOnlyCollection<CapturedRequest> Captured => _captured.ToArray();

    /// <summary>
    /// Most recent captured request (null when no calls have been made yet).
    /// </summary>
    public CapturedRequest? Last => _last;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        var signature = request.Headers.TryGetValues("X-PaymentHub-Signature", out var sigValues)
            ? string.Join(",", sigValues)
            : null;

        var timestamp = request.Headers.TryGetValues("X-PaymentHub-Timestamp", out var tsValues)
            ? string.Join(",", tsValues)
            : null;

        var captured = new CapturedRequest(
            Method: request.Method.Method,
            Url: request.RequestUri?.ToString() ?? string.Empty,
            SignatureHeader: signature,
            TimestampHeader: timestamp,
            Body: body);

        _captured.Enqueue(captured);
        _last = captured;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}