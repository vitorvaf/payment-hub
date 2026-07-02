using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PaymentHub.Api.Auth;
using PaymentHub.Application.Abstractions.Observability;
using PaymentHub.Application.Observability;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Api;

/// <summary>
/// Tests for <see cref="CorrelationIdMiddleware"/>. The middleware must
/// resolve a CorrelationId for every inbound request, never reject a
/// syntactically invalid header (slice 9-O1.1 decision #2), and never leak
/// the rejected value verbatim into the log stream.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldGenerateCorrelationId_WhenHeaderIsAbsent()
    {
        var http = BuildHttpContext(headers: null);
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        var middleware = BuildMiddleware(accessor, out var logger);

        await middleware.InvokeAsync(http, accessor);

        accessor.CorrelationId.Should().NotBeNullOrWhiteSpace();
        accessor.CorrelationId!.Should().HaveLength(32);
        http.Response.Headers[CorrelationIdGenerator.HeaderName].ToString()
            .Should().Be(accessor.CorrelationId);
    }

    [Fact]
    public async Task InvokeAsync_ShouldPreserveInboundCorrelationId_WhenValid()
    {
        var http = BuildHttpContext(headers: new Dictionary<string, string>
        {
            [CorrelationIdGenerator.HeaderName] = CorrelationIdTestHelper.ValidId
        });
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        var middleware = BuildMiddleware(accessor, out _);

        await middleware.InvokeAsync(http, accessor);

        accessor.CorrelationId.Should().Be(CorrelationIdTestHelper.ValidId);
        http.Response.Headers[CorrelationIdGenerator.HeaderName].ToString()
            .Should().Be(CorrelationIdTestHelper.ValidId);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSubstituteSilently_WhenHeaderIsInvalid()
    {
        var http = BuildHttpContext(headers: new Dictionary<string, string>
        {
            [CorrelationIdGenerator.HeaderName] = CorrelationIdTestHelper.InvalidId()
        });
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        var middleware = BuildMiddleware(accessor, out var logger);

        await middleware.InvokeAsync(http, accessor);

        accessor.CorrelationId.Should().NotBeNullOrWhiteSpace();
        accessor.CorrelationId.Should().NotBe(CorrelationIdTestHelper.InvalidId());
        accessor.CorrelationId!.Should().HaveLength(32);
        // Anti-leak: the rejected value MUST NOT appear in the response or
        // the accessor after the substitution.
        http.Response.Headers[CorrelationIdGenerator.HeaderName].ToString()
            .Should().NotBe(CorrelationIdTestHelper.InvalidId());
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotLogTheRejectedValue_WhenSubstituting()
    {
        // Slice 9-O1.4 anti-leak gate: the middleware MUST NOT echo the
        // user-controlled candidate value into the log stream.
        var http = BuildHttpContext(headers: new Dictionary<string, string>
        {
            [CorrelationIdGenerator.HeaderName] = CorrelationIdTestHelper.InvalidId()
        });
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        var sink = new InMemoryLoggerProvider();
        var middleware = BuildMiddleware(accessor, out _, sink);

        await middleware.InvokeAsync(http, accessor);

        var combined = string.Join(" | ", sink.Records.Select(r => r.Message));
        combined.Should().NotContain(CorrelationIdTestHelper.InvalidId());
    }

    [Fact]
    public async Task InvokeAsync_ShouldPopulateHttpContextItems_Slot()
    {
        var http = BuildHttpContext(headers: new Dictionary<string, string>
        {
            [CorrelationIdGenerator.HeaderName] = CorrelationIdTestHelper.ValidId
        });
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        var middleware = BuildMiddleware(accessor, out _);

        await middleware.InvokeAsync(http, accessor);

        http.Items.TryGetValue(CorrelationIdGenerator.HttpContextItemsKey, out var slot).Should().BeTrue();
        slot.Should().Be(CorrelationIdTestHelper.ValidId);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenConfigured()
    {
        // Build a middleware with a tracking next delegate to verify the
        // pipeline was awaited.
        var http = BuildHttpContext(headers: null);
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var logger = new LoggerFactory().CreateLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(next, logger);

        await middleware.InvokeAsync(http, accessor);

        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext BuildHttpContext(IDictionary<string, string>? headers)
    {
        var http = new DefaultHttpContext();
        if (headers is not null)
        {
            foreach (var kv in headers)
            {
                http.Request.Headers[kv.Key] = kv.Value;
            }
        }
        return http;
    }

    private static CorrelationIdMiddleware BuildMiddleware(
        ICorrelationIdAccessor accessor,
        out ILogger<CorrelationIdMiddleware> logger,
        InMemoryLoggerProvider? sink = null)
    {
        sink ??= new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();
        RequestDelegate next = _ => Task.CompletedTask;
        return new CorrelationIdMiddleware(next, logger);
    }
}
