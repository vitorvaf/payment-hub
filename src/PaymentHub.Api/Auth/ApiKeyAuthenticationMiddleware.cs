using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Observability;

namespace PaymentHub.Api.Auth;

public sealed class ApiKeyAuthenticationMiddleware
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";
    private const string TenantHeader = "X-Tenant-Id";
    private const string ApplicationHeader = "X-Application-Id";

    // Slice 9-O2: safe reason tags for AuthorizationDeniedTotal. These
    // are the only safe keys/motives the middleware emits; the actual API
    // key value never reaches the tag value (re-asserting anti-leak rules
    // from Slices 6-A, 9-O1.4).
    private const string ReasonMissingApiKey = "missing_api_key";
    private const string ReasonInvalidApiKey = "invalid_api_key";
    private const string ReasonInactiveTenant = "inactive_tenant";
    private const string ReasonInactiveApplication = "inactive_application";

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
            // Slice 9-O2: counter for missing/invalid auth header. The
            // bearer token is NEVER logged or passed as a tag value.
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonMissingApiKey));
            _logger.LogWarning("{Event} reason={Reason}", PaymentHubLogEvents.AuthDenied, ReasonMissingApiKey);
            await WriteUnauthorized(context);
            return;
        }

        var presentedKey = authHeader.ToString()[BearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonMissingApiKey));
            _logger.LogWarning("{Event} reason={Reason}", PaymentHubLogEvents.AuthDenied, ReasonMissingApiKey);
            await WriteUnauthorized(context);
            return;
        }

        var hash = hasher.Hash(presentedKey);
        var apiKey = await apiKeys.FindByHashAsync(hash, context.RequestAborted);
        if (apiKey is null || !apiKey.Active)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInvalidApiKey));
            _logger.LogWarning("{Event} reason={Reason}", PaymentHubLogEvents.AuthDenied, ReasonInvalidApiKey);
            await WriteUnauthorized(context);
            return;
        }

        var headerTenantId = context.Request.Headers[TenantHeader].ToString();
        var headerApplicationId = context.Request.Headers[ApplicationHeader].ToString();
        if (!Guid.TryParse(headerTenantId, out var tenantId) || tenantId == Guid.Empty)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInvalidApiKey));
            _logger.LogWarning("{Event} reason={Reason}", PaymentHubLogEvents.AuthDenied, ReasonInvalidApiKey);
            await WriteUnauthorized(context);
            return;
        }
        if (!Guid.TryParse(headerApplicationId, out var applicationId) || applicationId == Guid.Empty)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInvalidApiKey));
            _logger.LogWarning("{Event} reason={Reason}", PaymentHubLogEvents.AuthDenied, ReasonInvalidApiKey);
            await WriteUnauthorized(context);
            return;
        }

        if (apiKey.TenantId != tenantId || apiKey.ApplicationId != applicationId)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInvalidApiKey));
            _logger.LogWarning("{Event} reason={Reason}", PaymentHubLogEvents.AuthDenied, ReasonInvalidApiKey);
            await WriteUnauthorized(context);
            return;
        }

        var tenant = await tenants.GetByIdAsync(tenantId, context.RequestAborted);
        if (tenant is null)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInvalidApiKey));
            _logger.LogWarning(
                "{Event} reason={Reason} tenantId={TenantId} apiKeyId={ApiKeyId}",
                PaymentHubLogEvents.AuthDenied, ReasonInvalidApiKey, tenantId, apiKey.Id);
            await WriteUnauthorized(context);
            return;
        }
        if (tenant.Status != Domain.Enums.TenantStatus.Active)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInactiveTenant));
            _logger.LogWarning(
                "{Event} reason={Reason} tenantId={TenantId} apiKeyId={ApiKeyId} tenantStatus={TenantStatus}",
                PaymentHubLogEvents.AuthInactive, ReasonInactiveTenant, tenantId, apiKey.Id, tenant.Status);
            await WriteForbidden(context);
            return;
        }

        var application = await applications.GetByTenantAndIdAsync(tenantId, applicationId, context.RequestAborted);
        if (application is null)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInvalidApiKey));
            _logger.LogWarning(
                "{Event} reason={Reason} applicationId={ApplicationId} tenantId={TenantId} apiKeyId={ApiKeyId}",
                PaymentHubLogEvents.AuthDenied, ReasonInvalidApiKey, applicationId, tenantId, apiKey.Id);
            await WriteUnauthorized(context);
            return;
        }
        if (application.Status != Domain.Enums.ApplicationStatus.Active)
        {
            PaymentHubMetrics.AuthorizationDeniedTotal.Record(1,
                PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.ErrorCategory, ReasonInactiveApplication));
            _logger.LogWarning(
                "{Event} reason={Reason} applicationId={ApplicationId} tenantId={TenantId} apiKeyId={ApiKeyId} applicationStatus={ApplicationStatus}",
                PaymentHubLogEvents.AuthInactive, ReasonInactiveApplication, applicationId, tenantId, apiKey.Id, application.Status);
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