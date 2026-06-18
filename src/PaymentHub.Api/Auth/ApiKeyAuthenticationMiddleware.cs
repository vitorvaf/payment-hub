using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;

namespace PaymentHub.Api.Auth;

public sealed class ApiKeyAuthenticationMiddleware
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";
    private const string TenantHeader = "X-Tenant-Id";
    private const string ApplicationHeader = "X-Application-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApiKeyRepository apiKeys,
        IApiKeyHasher hasher,
        ITenantRepository tenants,
        IApplicationClientRepository applications)
    {
        if (IsAnonymousPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(AuthorizationHeader, out var authHeader)
            || string.IsNullOrWhiteSpace(authHeader.ToString())
            || !authHeader.ToString().StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorized(context);
            return;
        }

        var presentedKey = authHeader.ToString()[BearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            await WriteUnauthorized(context);
            return;
        }

        var hash = hasher.Hash(presentedKey);
        var apiKey = await apiKeys.FindByHashAsync(hash, context.RequestAborted);
        if (apiKey is null || !apiKey.Active)
        {
            await WriteUnauthorized(context);
            return;
        }

        var headerTenantId = context.Request.Headers[TenantHeader].ToString();
        var headerApplicationId = context.Request.Headers[ApplicationHeader].ToString();
        if (!Guid.TryParse(headerTenantId, out var tenantId) || tenantId == Guid.Empty)
        {
            await WriteUnauthorized(context);
            return;
        }
        if (!Guid.TryParse(headerApplicationId, out var applicationId) || applicationId == Guid.Empty)
        {
            await WriteUnauthorized(context);
            return;
        }

        if (apiKey.TenantId != tenantId || apiKey.ApplicationId != applicationId)
        {
            await WriteUnauthorized(context);
            return;
        }

        var tenant = await tenants.GetByIdAsync(tenantId, context.RequestAborted);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Rejected request for unknown tenant {TenantId} (apiKeyId {ApiKeyId}).",
                tenantId, apiKey.Id);
            await WriteUnauthorized(context);
            return;
        }
        if (tenant.Status != Domain.Enums.TenantStatus.Active)
        {
            _logger.LogWarning(
                "Rejected request for inactive tenant {TenantId} (apiKeyId {ApiKeyId}, status {TenantStatus}).",
                tenantId, apiKey.Id, tenant.Status);
            await WriteForbidden(context);
            return;
        }

        var application = await applications.GetByTenantAndIdAsync(tenantId, applicationId, context.RequestAborted);
        if (application is null)
        {
            _logger.LogWarning(
                "Rejected request for unknown application {ApplicationId} under tenant {TenantId} (apiKeyId {ApiKeyId}).",
                applicationId, tenantId, apiKey.Id);
            await WriteUnauthorized(context);
            return;
        }
        if (application.Status != Domain.Enums.ApplicationStatus.Active)
        {
            _logger.LogWarning(
                "Rejected request for inactive application {ApplicationId} under tenant {TenantId} (apiKeyId {ApiKeyId}, status {ApplicationStatus}).",
                applicationId, tenantId, apiKey.Id, application.Status);
            await WriteForbidden(context);
            return;
        }

        context.Items["tenantId"] = tenantId;
        context.Items["applicationId"] = applicationId;
        context.Items["apiKeyId"] = apiKey.Id;

        apiKey.Touch(DateTime.UtcNow);

        await _next(context);
    }

    private static bool IsAnonymousPath(PathString path)
    {
        if (!path.HasValue) return true;
        var p = path.Value!;
        return p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/api/v1/webhooks/", StringComparison.OrdinalIgnoreCase)
            || p == "/"
            || p == "/favicon.ico";
    }

    private static async Task WriteUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "unauthorized",
            message = "Unauthorized"
        });
    }

    private static async Task WriteForbidden(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "forbidden",
            message = "Client application is not allowed to access this resource."
        });
    }
}