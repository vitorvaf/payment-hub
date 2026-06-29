using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentHub.Infrastructure.Providers.AbacatePay;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Infrastructure.Providers.AbacatePay;

public class AbacatePayClientTests
{
    private const string ApiKey = "apx_test_key_1234567890";
    private const string ProviderPaymentId = "pix_abc_123";

    private static IOptionsMonitor<AbacatePayOptions> BuildOptions(bool allowSimulation = false)
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new AbacatePayOptions
        {
            BaseUrl = "https://api.abacatepay.com/v2",
            TimeoutSeconds = 5,
            AllowDevModeSimulation = allowSimulation
        });
        return new StaticOptionsMonitor<AbacatePayOptions>(opts.Value);
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldSendBearerHeaderAndParseBrCode()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(req =>
        {
            // Authorization header check.
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be(ApiKey);
            // Method + path.
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().Be("/v2/transparents/create");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"pix_abc","status":"PENDING","amount":1000,"brCode":"00020126...","brCodeBase64":"iVBORw0KGgo=","expiresAt":"2026-06-30T00:00:00Z","devMode":true},"success":true}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var resp = await client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest
            {
                AmountInCents = 1000,
                Description = "Test",
                ExpiresInSeconds = 3600
            },
            ApiKey,
            CancellationToken.None);

        resp.Id.Should().Be("pix_abc");
        resp.Status.Should().Be("PENDING");
        resp.BrCode.Should().StartWith("00020126");
        resp.BrCodeBase64.Should().Be("iVBORw0KGgo=");
        resp.AmountInCents.Should().Be(1000);
        resp.DevMode.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldSerializeAmountInCentsAndMetadata()
    {
        var handler = new ScriptedHandler();
        string? capturedBody = null;
        handler.Enqueue(req =>
        {
            capturedBody = handler.CapturedBodies[^1];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"pix_x","status":"PENDING"},"success":true}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        await client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest
            {
                AmountInCents = 9999,
                Description = "Pedido 42",
                Metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = "t1",
                    ["paymentId"] = "p1"
                }
            },
            ApiKey,
            CancellationToken.None);

        capturedBody.Should().NotBeNullOrWhiteSpace();
        capturedBody.Should().Contain("\"amount\":9999");
        capturedBody.Should().Contain("\"tenantId\":\"t1\"");
        capturedBody.Should().Contain("\"paymentId\":\"p1\"");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AbacatePayErrorCategory.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, AbacatePayErrorCategory.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, AbacatePayErrorCategory.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, AbacatePayErrorCategory.NotFound, false)]
    [InlineData(HttpStatusCode.TooManyRequests, AbacatePayErrorCategory.RateLimited, true)]
    [InlineData(HttpStatusCode.InternalServerError, AbacatePayErrorCategory.ServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, AbacatePayErrorCategory.ServerError, true)]
    public async Task CreateTransparentPixAsync_ShouldCategorizeHttpFailures(
        HttpStatusCode statusCode, AbacatePayErrorCategory expectedCategory, bool expectedTransient)
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("""{"data":null,"success":false,"error":"boom"}""", Encoding.UTF8, "application/json")
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var act = async () => await client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            ApiKey,
            CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<AbacatePayClientException>()).Which;
        ex.Category.Should().Be(expectedCategory);
        ex.StatusCode.Should().Be((int)statusCode);
        ex.IsTransient.Should().Be(expectedTransient);
        ex.Message.Should().NotContain(ApiKey);
        ex.Message.Should().NotContain("brCodeBase64");
        ex.Message.Should().NotContain("boom"); // raw body must not leak
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldMapHttpRequestExceptionToNetwork()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueThrow(new HttpRequestException("connection refused"));

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() => client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            ApiKey,
            CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.Network);
        ex.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldMapTimeoutToTimeoutCategory()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueThrow(new TaskCanceledException("timeout", new TimeoutException()));

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() => client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            ApiKey,
            CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.Timeout);
        ex.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldHonorCallerCancellation()
    {
        var handler = new ScriptedHandler();
        handler.EnqueueThrow(new TaskCanceledException("user cancel"));

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancellation should propagate as OperationCanceledException, not
        // be wrapped in AbacatePayClientException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            ApiKey,
            cts.Token));
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldMapEnvelopeFailure()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":false,"error":"provider declined"}""", Encoding.UTF8, "application/json")
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() => client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            ApiKey,
            CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.EnvelopeFailure);
        ex.StatusCode.Should().Be(200);
        ex.IsTransient.Should().BeFalse();
        ex.Message.Should().NotContain("provider declined");
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldMapMalformedJsonToEnvelopeFailure()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() => client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            ApiKey,
            CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.EnvelopeFailure);
    }

    [Fact]
    public async Task CreateTransparentPixAsync_ShouldThrowBadRequestWhenApiKeyMissing()
    {
        var handler = new ScriptedHandler();
        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() => client.CreateTransparentPixAsync(
            new AbacatePayCreateTransparentPixRequest { AmountInCents = 1, Description = "x" },
            apiKey: "  ",
            CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.Unauthorized);
        handler.Requests.Should().BeEmpty(); // no HTTP request issued
    }

    [Fact]
    public async Task CheckTransparentPixAsync_ShouldSendGetWithIdQuery()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(req =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.Query.Should().Contain("id=" + ProviderPaymentId);
            req.Headers.Authorization!.Parameter.Should().Be(ApiKey);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"pix_abc","status":"PAID","amount":1000,"paidAt":"2026-06-28T12:00:00Z"},"success":true}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var resp = await client.CheckTransparentPixAsync(ProviderPaymentId, ApiKey, CancellationToken.None);

        resp.Id.Should().Be("pix_abc");
        resp.Status.Should().Be("PAID");
        resp.PaidAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("PAID")]
    [InlineData("EXPIRED")]
    [InlineData("CANCELLED")]
    public async Task CheckTransparentPixAsync_ShouldMapAllKnownStatuses(string providerStatus)
    {
        var handler = new ScriptedHandler();
        var body = "{\"data\":{\"id\":\"pix\",\"status\":\"" + providerStatus + "\"},\"success\":true}";
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var resp = await client.CheckTransparentPixAsync(ProviderPaymentId, ApiKey, CancellationToken.None);

        resp.Status.Should().Be(providerStatus);
    }

    [Fact]
    public async Task CheckTransparentPixAsync_ShouldRejectEmptyId()
    {
        var handler = new ScriptedHandler();
        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() =>
            client.CheckTransparentPixAsync(" ", ApiKey, CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.BadRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulateTransparentPixPaymentAsync_ShouldBeDisabledByDefault()
    {
        var handler = new ScriptedHandler();
        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(allowSimulation: false), NullLogger<AbacatePayClient>.Instance);

        var ex = (await Assert.ThrowsAsync<AbacatePayClientException>(() =>
            client.SimulateTransparentPixPaymentAsync(ProviderPaymentId, ApiKey, CancellationToken.None)));

        ex.Category.Should().Be(AbacatePayErrorCategory.SimulationDisabled);
        ex.IsTransient.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulateTransparentPixPaymentAsync_ShouldCallEndpointWhenEnabled()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().Be("/v2/transparents/simulate-payment");
            req.RequestUri.Query.Should().Contain("id=" + ProviderPaymentId);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"pix_abc","status":"PAID"},"success":true}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var client = new AbacatePayClient(factory, BuildOptions(allowSimulation: true), NullLogger<AbacatePayClient>.Instance);

        var resp = await client.SimulateTransparentPixPaymentAsync(ProviderPaymentId, ApiKey, CancellationToken.None);

        resp.Status.Should().Be("PAID");
        resp.Id.Should().Be("pix_abc");
    }

    /// <summary>
    /// Tiny <see cref="IHttpClientFactory"/> for tests that returns a
    /// pre-configured handler. Mirrors the pattern used by
    /// <c>HttpApplicationWebhookDispatcher</c> tests. Avoids depending on
    /// the default <c>IHttpClientFactory</c> implementation which would
    /// create a separate handler pipeline. Sets <see cref="HttpClient.BaseAddress"/>
    /// so the AbacatePay client can use relative paths like
    /// <c>/transparents/create</c> without an absolute URI in the request.
    /// </summary>
    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private static readonly Uri BaseAddress = new("https://api.abacatepay.com/v2/");
        private readonly HttpMessageHandler _handler;
        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false) { BaseAddress = BaseAddress };
    }

    /// <summary>
    /// <see cref="IOptionsMonitor{T}"/> shim that always returns the same
    /// value. The AbacatePay client only reads <c>CurrentValue</c>, so the
    /// change notification surface is not exercised here.
    /// </summary>
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}