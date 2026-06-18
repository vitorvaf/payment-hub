using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentHub.Api.Auth;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Entities;

namespace PaymentHub.UnitTests.Api;

public class ApiKeyAuthenticationMiddlewareTests
{
    private const string TenantHeader = "X-Tenant-Id";
    private const string ApplicationHeader = "X-Application-Id";
    private const string AuthorizationHeader = "Authorization";

    private static async Task<HttpContext> BuildContext(
        string? authorization,
        string? tenantHeader,
        string? applicationHeader,
        IApiKeyRepository apiKeys,
        IApiKeyHasher hasher,
        ITenantRepository tenants,
        IApplicationClientRepository applications,
        string path = "/api/v1/payments")
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = path;
        ctx.Request.Method = "GET";
        if (authorization is not null) ctx.Request.Headers[AuthorizationHeader] = authorization;
        if (tenantHeader is not null) ctx.Request.Headers[TenantHeader] = tenantHeader;
        if (applicationHeader is not null) ctx.Request.Headers[ApplicationHeader] = applicationHeader;

        bool nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyAuthenticationMiddleware(next, NullLogger<ApiKeyAuthenticationMiddleware>.Instance);
        await middleware.InvokeAsync(ctx, apiKeys, hasher, tenants, applications);
        ctx.Items["__nextCalled"] = nextCalled;
        return ctx;
    }

    private static (Mock<IApiKeyRepository>, Mock<IApiKeyHasher>, Mock<ITenantRepository>, Mock<IApplicationClientRepository>)
        BuildRepositoryMocks(HashedApiKeySeed? seed = null)
    {
        var apiKeys = new Mock<IApiKeyRepository>(MockBehavior.Strict);
        var hasher = new Mock<IApiKeyHasher>();
        var tenants = new Mock<ITenantRepository>(MockBehavior.Strict);
        var applications = new Mock<IApplicationClientRepository>(MockBehavior.Strict);

        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hash");

        if (seed is not null)
        {
            var apiKey = new ApiKey(seed.ApiKeyId, seed.TenantId, seed.ApplicationId, "Test", "hash", "phk_xxxx");
            apiKeys.Setup(r => r.FindByHashAsync("hash", It.IsAny<CancellationToken>()))
                .ReturnsAsync(apiKey);

            if (seed.Tenant is not null)
            {
                tenants.Setup(r => r.GetByIdAsync(seed.TenantId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(seed.Tenant);
            }
            else
            {
                tenants.Setup(r => r.GetByIdAsync(seed.TenantId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Tenant?)null);
            }

            if (seed.Application is not null)
            {
                applications.Setup(r => r.GetByTenantAndIdAsync(seed.TenantId, seed.ApplicationId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(seed.Application);
            }
            else
            {
                applications.Setup(r => r.GetByTenantAndIdAsync(seed.TenantId, seed.ApplicationId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((ApplicationClient?)null);
            }
        }
        else
        {
            apiKeys.Setup(r => r.FindByHashAsync("hash", It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApiKey?)null);
        }

        return (apiKeys, hasher, tenants, applications);
    }

    private sealed record HashedApiKeySeed(
        Guid ApiKeyId,
        Guid TenantId,
        Guid ApplicationId,
        Tenant? Tenant,
        ApplicationClient? Application);

    [Fact]
    public async Task AnonymousPath_ShouldNotRequireApiKey()
    {
        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks();
        var ctx = await BuildContext(null, null, null, apiKeys.Object, hasher.Object, tenants.Object, applications.Object, path: "/health");

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Items["__nextCalled"].Should().Be(true);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_ShouldReturn401()
    {
        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks();
        var ctx = await BuildContext(null, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Items["__nextCalled"].Should().Be(false);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
        ctx.Items.ContainsKey("applicationId").Should().BeFalse();
    }

    [Fact]
    public async Task InvalidApiKey_ShouldReturn401()
    {
        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks();
        var ctx = await BuildContext("Bearer presented",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Items["__nextCalled"].Should().Be(false);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
    }

    [Fact]
    public async Task ValidApiKey_AndActiveTenantAndApplication_ShouldSetContextAndProceed()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "Tenant", "tenant");
        var application = new ApplicationClient(appId, tenantId, "App");

        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks(
            new HashedApiKeySeed(apiKeyId, tenantId, appId, tenant, application));

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Items["__nextCalled"].Should().Be(true);
        ctx.Items["tenantId"].Should().Be(tenantId);
        ctx.Items["applicationId"].Should().Be(appId);
        ctx.Items["apiKeyId"].Should().Be(apiKeyId);
    }

    [Fact]
    public async Task TenantMismatch_ShouldReturn401()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Test", "hash", "phk_xxx");

        var hasher = new Mock<IApiKeyHasher>();
        hasher.Setup(h => h.Hash("presented")).Returns("hash");

        var apiKeys = new Mock<IApiKeyRepository>();
        apiKeys.Setup(r => r.FindByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        var tenants = new Mock<ITenantRepository>(MockBehavior.Strict);
        var applications = new Mock<IApplicationClientRepository>(MockBehavior.Strict);

        var ctx = await BuildContext("Bearer presented",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
    }

    [Fact]
    public async Task InactiveTenant_ShouldReturn403AndNotProceed()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "Tenant", "tenant");
        tenant.Suspend();

        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks(
            new HashedApiKeySeed(apiKeyId, tenantId, appId, tenant, null));

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(403);
        ctx.Items["__nextCalled"].Should().Be(false);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
        ctx.Items.ContainsKey("applicationId").Should().BeFalse();
        ctx.Items.ContainsKey("apiKeyId").Should().BeFalse();
    }

    [Fact]
    public async Task InactiveApplication_ShouldReturn403AndNotProceed()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "Tenant", "tenant");
        var application = new ApplicationClient(appId, tenantId, "App");
        application.Suspend();

        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks(
            new HashedApiKeySeed(apiKeyId, tenantId, appId, tenant, application));

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(403);
        ctx.Items["__nextCalled"].Should().Be(false);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
        ctx.Items.ContainsKey("applicationId").Should().BeFalse();
        ctx.Items.ContainsKey("apiKeyId").Should().BeFalse();
    }

    [Fact]
    public async Task UnknownTenant_ShouldReturn401AndNotProceed()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();

        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks(
            new HashedApiKeySeed(apiKeyId, tenantId, appId, null, null));

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Items["__nextCalled"].Should().Be(false);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
    }

    [Fact]
    public async Task UnknownApplication_ShouldReturn401AndNotProceed()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "Tenant", "tenant");

        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks(
            new HashedApiKeySeed(apiKeyId, tenantId, appId, tenant, null));

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Items["__nextCalled"].Should().Be(false);
        ctx.Items.ContainsKey("tenantId").Should().BeFalse();
        ctx.Items.ContainsKey("applicationId").Should().BeFalse();
    }

    [Fact]
    public async Task RejectionResponses_ShouldNotLeakApiKeyOrEntityStatus()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "Tenant", "tenant");
        tenant.Suspend();

        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks(
            new HashedApiKeySeed(apiKeyId, tenantId, appId, tenant, null));

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(403);
        ctx.Response.ContentType.Should().StartWith("application/json");

        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        var root = doc.RootElement;
        root.GetProperty("error").GetString().Should().Be("forbidden");
        var raw = root.GetRawText();
        raw.Should().NotContain("presented");
        raw.Should().NotContain(tenantId.ToString());
        raw.Should().NotContain(appId.ToString());
        raw.Should().NotContain("Suspended");
        raw.Should().NotContain("Inactive");
        raw.Should().NotContain("tenant");
        raw.Should().NotContain("Tenant");
    }

    [Fact]
    public async Task UnauthorizedResponses_ShouldNotLeakApiKeyOrEntityStatus()
    {
        var (apiKeys, hasher, tenants, applications) = BuildRepositoryMocks();

        var ctx = await BuildContext("Bearer presented",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object, tenants.Object, applications.Object);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        var raw = doc.RootElement.GetRawText();
        raw.Should().NotContain("presented");
    }
}