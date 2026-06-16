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
    private static async Task<HttpContext> BuildContext(
        string? authorization,
        string? tenantHeader,
        string? applicationHeader,
        IApiKeyRepository apiKeys,
        IApiKeyHasher hasher,
        string path = "/api/v1/payments")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "GET";
        if (authorization is not null) ctx.Request.Headers["Authorization"] = authorization;
        if (tenantHeader is not null) ctx.Request.Headers["X-Tenant-Id"] = tenantHeader;
        if (applicationHeader is not null) ctx.Request.Headers["X-Application-Id"] = applicationHeader;

        bool nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyAuthenticationMiddleware(next);
        await middleware.InvokeAsync(ctx, apiKeys, hasher);
        ctx.Items["__nextCalled"] = nextCalled;
        return ctx;
    }

    [Fact]
    public async Task AnonymousPath_ShouldNotRequireApiKey()
    {
        var apiKeys = new Mock<IApiKeyRepository>(MockBehavior.Strict);
        var hasher = new Mock<IApiKeyHasher>(MockBehavior.Strict);
        var ctx = await BuildContext(null, null, null, apiKeys.Object, hasher.Object, path: "/health");

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Items["__nextCalled"].Should().Be(true);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_ShouldReturn401()
    {
        var apiKeys = new Mock<IApiKeyRepository>();
        var hasher = new Mock<IApiKeyHasher>();
        var ctx = await BuildContext(null, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvalidApiKey_ShouldReturn401()
    {
        var hasher = new Mock<IApiKeyHasher>();
        hasher.Setup(h => h.Hash("presented")).Returns("hash");

        var apiKeys = new Mock<IApiKeyRepository>();
        apiKeys.Setup(r => r.FindByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey?)null);

        var ctx = await BuildContext("Bearer presented",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ValidApiKey_ShouldSetTenantAndApplicationInContext()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var apiKey = new ApiKey(apiKeyId, tenantId, appId, "Test", "hash", "phk_xxxx");
        apiKey.GetType(); // touch

        var hasher = new Mock<IApiKeyHasher>();
        hasher.Setup(h => h.Hash("presented")).Returns("hash");

        var apiKeys = new Mock<IApiKeyRepository>();
        apiKeys.Setup(r => r.FindByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        var ctx = await BuildContext("Bearer presented", tenantId.ToString(), appId.ToString(),
            apiKeys.Object, hasher.Object);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Items["tenantId"].Should().Be(tenantId);
        ctx.Items["applicationId"].Should().Be(appId);
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

        var ctx = await BuildContext("Bearer presented",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            apiKeys.Object, hasher.Object);

        ctx.Response.StatusCode.Should().Be(401);
    }
}
