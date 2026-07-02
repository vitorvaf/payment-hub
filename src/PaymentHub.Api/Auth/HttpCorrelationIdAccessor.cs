using PaymentHub.Application.Abstractions.Observability;
using PaymentHub.Application.Observability;

namespace PaymentHub.Api.Auth;

/// <summary>
/// HTTP-aware <see cref="ICorrelationIdAccessor"/> implementation. Reads and
/// writes the <c>HttpContext.Items["correlationId"]</c> slot populated by
/// <see cref="CorrelationIdMiddleware"/>. The accessor is registered as
/// <c>Scoped</c> so it shares the lifetime of the HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// Background code (workers, hosted services) cannot resolve this accessor
/// because there is no <see cref="IHttpContextAccessor"/> available — they
/// receive a <c>null</c> accessor instead. See <c>NullCorrelationIdAccessor</c>
/// in <c>PaymentHub.Worker</c> for the worker side.
/// </para>
/// </remarks>
public sealed class HttpCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CorrelationId
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            if (http is null) return null;
            return http.Items.TryGetValue(CorrelationIdGenerator.HttpContextItemsKey, out var raw)
                ? raw as string
                : null;
        }
    }

    public void Set(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var http = _httpContextAccessor.HttpContext;
        if (http is null) return;
        http.Items[CorrelationIdGenerator.HttpContextItemsKey] = id;
    }
}
