using Microsoft.AspNetCore.Http;
using PaymentHub.Application.Abstractions.Observability;
using PaymentHub.Application.Observability;
using Serilog.Context;

namespace PaymentHub.Api.Auth;

/// <summary>
/// Resolves the request-scoped <c>CorrelationId</c> for every inbound HTTP
/// request. Slice 9-O1 introduces it so logs, downstream handlers and the
/// Outbox dispatcher share a single identifier end-to-end.
///
/// <para>
/// The middleware reads the inbound <c>X-Correlation-Id</c> header. When the
/// header is missing or syntactically invalid, a fresh GUID is generated and
/// substituted silently — we never return <c>400</c> for a bad correlation id
/// because the header is informational and not part of the API contract
/// (decision #2 in the slice plan). The resolved id is:
/// </para>
/// <list type="bullet">
/// <item>Stored in <c>HttpContext.Items["correlationId"]</c> for downstream
/// code (<c>HttpTenantContext</c> already reads sibling keys in the same
/// dictionary).</item>
/// <item>Pushed onto the Serilog <c>LogContext</c> as
/// <see cref="CorrelationIdGenerator.HeaderName"/> so every log line emitted
/// while the request is in scope carries the id.</item>
/// <item>Echoed back in the response so clients can correlate their own
/// requests with Payment Hub logs.</item>
/// </list>
///
/// <para>
/// <b>Anti-leak</b>: the middleware does NOT log the candidate header value
/// when rejecting an invalid one. Rejections produce a single Information
/// log line that mentions only the request path and the fact that a fresh id
/// was generated — the existing safe-log policy from <c>docs/specs/011</c>
/// forbids leaking user-controlled values verbatim into structured logs.
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor accessor)
    {
        var resolved = ResolveOrGenerate(context);
        accessor.Set(resolved);

        context.Items[CorrelationIdGenerator.HttpContextItemsKey] = resolved;
        context.Response.Headers[CorrelationIdGenerator.HeaderName] = resolved;

        using (LogContext.PushProperty(CorrelationIdGenerator.HeaderName, resolved))
        {
            await _next(context);
        }
    }

    private string ResolveOrGenerate(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdGenerator.HeaderName, out var values))
        {
            // HttpRequestHeaders stores a StringValues; FirstOrDefault gives us
            // the first non-empty token, ignoring duplicates without throwing.
            var candidate = values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (CorrelationIdGenerator.IsValid(candidate))
            {
                return candidate!;
            }

            _logger.LogInformation(
                "Inbound {Header} header present but invalid; substituting a fresh correlation id for path {Path}.",
                CorrelationIdGenerator.HeaderName,
                context.Request.Path);
        }

        return CorrelationIdGenerator.New();
    }
}
