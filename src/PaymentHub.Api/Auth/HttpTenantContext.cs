using PaymentHub.Application.Abstractions.Context;

namespace PaymentHub.Api.Auth;

public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId
    {
        get
        {
            var http = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context available.");
            if (!http.Items.TryGetValue("tenantId", out var raw) || raw is not Guid g || g == Guid.Empty)
                throw new InvalidOperationException("Tenant id not resolved.");
            return g;
        }
    }

    public Guid ApplicationId
    {
        get
        {
            var http = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP context available.");
            if (!http.Items.TryGetValue("applicationId", out var raw) || raw is not Guid g || g == Guid.Empty)
                throw new InvalidOperationException("Application id not resolved.");
            return g;
        }
    }
}
