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

    public ApiKeyAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApiKeyRepository apiKeys,
        IApiKeyHasher hasher)
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
            await WriteUnauthorized(context, "Missing or invalid Authorization header.");
            return;
        }

        var presentedKey = authHeader.ToString()[BearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            await WriteUnauthorized(context, "Empty API key.");
            return;
        }

        var hash = hasher.Hash(presentedKey);
        var apiKey = await apiKeys.FindByHashAsync(hash, context.RequestAborted);
        if (apiKey is null || !apiKey.Active)
        {
            await WriteUnauthorized(context, "Invalid API key.");
            return;
        }

        var headerTenantId = context.Request.Headers[TenantHeader].ToString();
        var headerApplicationId = context.Request.Headers[ApplicationHeader].ToString();
        if (!Guid.TryParse(headerTenantId, out var tenantId) || tenantId == Guid.Empty)
        {
            await WriteUnauthorized(context, "Missing or invalid X-Tenant-Id header.");
            return;
        }
        if (!Guid.TryParse(headerApplicationId, out var applicationId) || applicationId == Guid.Empty)
        {
            await WriteUnauthorized(context, "Missing or invalid X-Application-Id header.");
            return;
        }

        if (apiKey.TenantId != tenantId || apiKey.ApplicationId != applicationId)
        {
            await WriteUnauthorized(context, "API key does not match tenant or application.");
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

    private static async Task WriteUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "unauthorized",
            message
        });
    }
}
