using System.Net;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// HTTP message handler that serves a scripted queue of response factories. Each
/// <see cref="HttpRequestMessage"/> the dispatcher sends is captured, then the next queued
/// factory is invoked to produce a response. Lets tests cover success, failures and edge cases
/// without real network or static stub methods scattered across test files.
/// </summary>
/// <remarks>
/// Typical usage:
/// <code>
/// var handler = new ScriptedHandler();
/// handler.Enqueue(req => new HttpResponseMessage(HttpStatusCode.OK));
/// handler.Enqueue(req => new HttpResponseMessage(HttpStatusCode.InternalServerError));
/// handler.EnqueueThrow(new HttpRequestException("connection refused"));
///
/// var factory = new SingleHandlerHttpClientFactory(handler);
/// var dispatcher = new HttpApplicationWebhookDispatcher(factory, ...);
///
/// await dispatcher.DispatchAsync(outbox, default); // 200
/// await dispatcher.DispatchAsync(outbox, default); // 500
/// await dispatcher.DispatchAsync(outbox, default); // throws HttpRequestException
/// </code>
/// </remarks>
public sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly Queue<Func<HttpRequestMessage, Exception>> _exceptions = new();

    /// <summary>
    /// Requests received by this handler, in dispatch order. Useful for asserting headers,
    /// body, and order without re-reading the response queue.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// Bodies captured at the moment each request arrived. Necessary because <c>HttpClient</c>
    /// disposes its <c>HttpRequestMessage</c> (and therefore its <c>Content</c>) right after the
    /// handler returns, so reading the body later throws <c>ObjectDisposedException</c>.
    /// </summary>
    public List<string> CapturedBodies { get; } = new();

    public ScriptedHandler Enqueue(HttpStatusCode statusCode)
        => Enqueue(_ => new HttpResponseMessage(statusCode));

    public ScriptedHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    /// <summary>
    /// Enqueue an exception to be thrown from <see cref="SendAsync"/>. Mirrors what real
    /// HTTP layers raise on connection refused / TLS handshake failure / timeouts.
    /// </summary>
    public ScriptedHandler EnqueueThrow(Exception exception)
    {
        _exceptions.Enqueue(_ => exception);
        return this;
    }

    public ScriptedHandler EnqueueThrow(Func<HttpRequestMessage, Exception> factory)
    {
        _exceptions.Enqueue(factory);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        // Capture the body before HttpClient disposes the request when our task completes.
        CapturedBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync());

        if (_exceptions.Count > 0)
        {
            var ex = _exceptions.Dequeue();
            throw ex(request);
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"ScriptedHandler received an unexpected request to {request.RequestUri} " +
                $"but no more responses were queued.");
        }

        var factory = _responses.Dequeue();
        return factory(request);
    }
}