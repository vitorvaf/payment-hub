using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PaymentHub.Api.Auth;
using PaymentHub.Application.Observability;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Api;

/// <summary>
/// Tests for <see cref="HttpCorrelationIdAccessor"/>. The accessor reads and
/// writes the <c>HttpContext.Items["correlationId"]</c> slot populated by
/// <see cref="CorrelationIdMiddleware"/>. Tests assert the accessor
/// surfaces the same value the middleware stored.
/// </summary>
public class HttpCorrelationIdAccessorTests
{
    [Fact]
    public void CorrelationId_ShouldReturnNull_WhenHttpContextIsAbsent()
    {
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor());

        accessor.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void CorrelationId_ShouldReturnValueFromHttpContextItems_WhenPopulated()
    {
        var http = new DefaultHttpContext();
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        http.Items[CorrelationIdGenerator.HttpContextItemsKey] = CorrelationIdTestHelper.ValidId;

        accessor.CorrelationId.Should().Be(CorrelationIdTestHelper.ValidId);
    }

    [Fact]
    public void Set_ShouldStoreValueInHttpContextItems()
    {
        var http = new DefaultHttpContext();
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });

        accessor.Set(CorrelationIdTestHelper.ValidId);

        accessor.CorrelationId.Should().Be(CorrelationIdTestHelper.ValidId);
        http.Items[CorrelationIdGenerator.HttpContextItemsKey].Should().Be(CorrelationIdTestHelper.ValidId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_ShouldIgnoreNullOrWhitespaceValues(string? candidate)
    {
        var http = new DefaultHttpContext();
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = http });
        http.Items[CorrelationIdGenerator.HttpContextItemsKey] = CorrelationIdTestHelper.ValidId;

        accessor.Set(candidate!);

        // Existing value must NOT be overwritten by null/whitespace.
        accessor.CorrelationId.Should().Be(CorrelationIdTestHelper.ValidId);
    }

    [Fact]
    public void Set_ShouldSilentlyNoOp_WhenHttpContextIsAbsent()
    {
        var accessor = new HttpCorrelationIdAccessor(new HttpContextAccessor());

        var act = () => accessor.Set(CorrelationIdTestHelper.ValidId);

        act.Should().NotThrow();
        accessor.CorrelationId.Should().BeNull();
    }
}
