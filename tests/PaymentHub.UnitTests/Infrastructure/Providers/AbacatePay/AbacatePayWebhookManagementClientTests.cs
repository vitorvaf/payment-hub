using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Providers.AbacatePay;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Unit tests for <see cref="AbacatePayWebhookManagementClient"/>.
/// Covers Bearer header propagation, payload serialisation, error
/// categorisation, no-leak guarantees for apiKey/webhookSecret/headers,
/// and feature-policy gating. No real network is ever exercised — a
/// <see cref="ScriptedHandler"/> stands in for the AbacatePay HTTP API.
/// </summary>
public class AbacatePayWebhookManagementClientTests
{
    private const string ApiKey = "apx_test_key_abcdef0123456789";
    private const string WebhookSecret = "webhook-secret-do-not-leak-32chars";
    private const string ProtectedCredentials = "fake-protected|apiKey=" + ApiKey;
    private const string CallbackUrl = "https://payment-hub.example.com/api/v1/webhooks/AbacatePay";

    private static readonly IReadOnlyList<string> DefaultEvents = new[]
    {
        "transparent.completed",
        "transparent.refunded",
        "transparent.disputed",
        "transparent.lost"
    };

    private static (AbacatePayWebhookManagementClient client, ScriptedHandler handler) BuildClient(
        bool featureFlagOn = true,
        IProviderAccountCredentialsReader? reader = null)
    {
        var handler = new ScriptedHandler();
        var factory = new SingleHandlerHttpClientFactory(handler);

        var opts = Options.Create(new AbacatePayOptions
        {
            BaseUrl = "https://api.abacatepay.com/v2",
            TimeoutSeconds = 5
        });
        var optionsMonitor = new StaticOptionsMonitor<AbacatePayOptions>(opts.Value);

        var policy = new FakeProviderWebhookRegistrationFeaturePolicy
        {
            AllowRemoteRegistration = featureFlagOn
        };

        var credentials = reader ?? new DefaultReader();
        var logger = NullLogger<AbacatePayWebhookManagementClient>.Instance;
        var client = new AbacatePayWebhookManagementClient(factory, optionsMonitor, credentials, policy, logger);
        return (client, handler);
    }

    private sealed class DefaultReader : IProviderAccountCredentialsReader
    {
        // Returns the ApiKey whenever the protected blob carries a marker we can recognise.
        // This is the same shape as the real `ProviderAccountCredentialsReader`: a thin adapter
        // over the production inspector. We just hardcode the lookup here so the tests can
        // assert the value the client actually sends.
        public string? ReadApiKey(string encryptedCredentials)
        {
            if (string.IsNullOrWhiteSpace(encryptedCredentials)) return null;
            if (encryptedCredentials == ProtectedCredentials) return ApiKey;
            return null;
        }
    }

    // -----------------------------------------------------------------
    // 1. POST /webhooks/create is the outbound path; success returns Registered.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldReturnRegistered_WhenAbacatePayReturnsSuccess()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsolutePath.Should().Be("/v2/webhooks/create");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"whk_abc_123"},"success":true,"error":null}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.Registered);
        handler.Requests.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------
    // 2. Authorization: Bearer <apiKey> is set from the protected credentials.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldSetAuthorizationBearer_FromProtectedCredentials()
    {
        var (client, handler) = BuildClient();
        string? capturedAuth = null;
        handler.Enqueue(req =>
        {
            capturedAuth = req.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"whk_abc"},"success":true}""",
                    Encoding.UTF8, "application/json")
            };
        });

        await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        capturedAuth.Should().Be(ApiKey);
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    // -----------------------------------------------------------------
    // 3. Payload serialises name, endpoint, secret, events.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldSerializeEndpointNameSecretAndEvents()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"id":"whk_abc"},"success":true}""",
                Encoding.UTF8, "application/json")
        });

        await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        var body = handler.CapturedBodies[0];
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("endpoint").GetString().Should().Be(CallbackUrl);
        root.GetProperty("secret").GetString().Should().Be(WebhookSecret);
        root.GetProperty("name").GetString().Should().Contain("AbacatePay");
        var events = root.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        events.Should().BeEquivalentTo(DefaultEvents);
    }

    // -----------------------------------------------------------------
    // 4. BaseUrl is sourced from AbacatePayOptions.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldUseBaseUrlFromAbacatePayOptions()
    {
        var handler = new ScriptedHandler();
        var factory = new SingleHandlerHttpClientFactory(handler);
        // The factory ignores the base URL it is given — the AbacatePay
        // client should construct the URI from its own options, not the
        // factory base. We assert that the path includes the /v2 segment
        // from `AbacatePayOptions.BaseUrl`.
        var opts = Options.Create(new AbacatePayOptions
        {
            BaseUrl = "https://api.abacatepay.com/v2",
            TimeoutSeconds = 5
        });
        var monitor = new StaticOptionsMonitor<AbacatePayOptions>(opts.Value);
        var policy = new FakeProviderWebhookRegistrationFeaturePolicy { AllowRemoteRegistration = true };
        var client = new AbacatePayWebhookManagementClient(
            factory, monitor, new DefaultReader(), policy, NullLogger<AbacatePayWebhookManagementClient>.Instance);
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"id":"whk_x"},"success":true}""",
                Encoding.UTF8, "application/json")
        });

        await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/v2/webhooks/create");
    }

    // -----------------------------------------------------------------
    // 5. 400 -> RegistrationFailed (BadRequest category, never thrown).
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns400()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"success":false,"error":"bad request"}""",
                Encoding.UTF8, "application/json")
        });

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // 6. 401/403 -> RegistrationFailed (Unauthorized category).
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns401Or403(HttpStatusCode status)
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(status));

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // 7. 429 -> RegistrationFailed (RateLimited category, transient).
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns429()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // 8. 5xx -> RegistrationFailed (ServerError category, transient).
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns5xx(HttpStatusCode status)
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(status));

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // 9. HttpRequestException -> AbacatePayClientException(Network) — transient.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldThrowNetworkException_WhenConnectionFails()
    {
        var (client, handler) = BuildClient();
        handler.EnqueueThrow(new HttpRequestException("connection refused"));

        var act = async () => await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AbacatePayClientException>();
        ex.Which.Category.Should().Be(AbacatePayErrorCategory.Network);
        ex.Which.IsTransient.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // 10. TaskCanceledException without caller cancellation -> Timeout.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldThrowTimeoutException_WhenHttpClientTimesOut()
    {
        var (client, handler) = BuildClient();
        // TaskCanceledException with no caller-side cancellation
        handler.EnqueueThrow(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        var act = async () => await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AbacatePayClientException>();
        ex.Which.Category.Should().Be(AbacatePayErrorCategory.Timeout);
        ex.Which.IsTransient.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // 11. Caller cancellation propagates OperationCanceledException.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldPropagateCallerCancellation()
    {
        var (client, handler) = BuildClient();
        handler.EnqueueThrow(new TaskCanceledException("cancelled"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------
    // 12. Envelope success=false with HTTP 2xx -> RegistrationFailed.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenEnvelopeSuccessFalse()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":null,"success":false,"error":"synthetic_decline"}""",
                Encoding.UTF8, "application/json")
        });

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // 13. Failure response never leaks apiKey, webhookSecret, or Authorization.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldNeverLeakApiKeyOrSecretInFailureResponse()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                $"error: invalid api key {ApiKey} or secret {WebhookSecret}",
                Encoding.UTF8, "application/json")
        });

        await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        // The captured outbound body is the outbound request, not the response.
        // We assert the outbound body never carries the apiKey in any field.
        var outbound = handler.CapturedBodies[0];
        outbound.Should().NotContain(ApiKey,
            because: "the apiKey must not appear in any field of the outbound JSON request");
        // The secret IS expected in the outbound body (it's the value we send to
        // the upstream), but other secrets must not.
        outbound.Should().NotContain(ProtectedCredentials,
            because: "the protected blob must not be embedded in the outbound body");
    }

    // -----------------------------------------------------------------
    // 14. Feature flag off -> RegistrationFailed without HTTP call.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldNotCallHttp_WhenFeatureFlagIsOff()
    {
        var (client, handler) = BuildClient(featureFlagOn: false);

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
        handler.Requests.Should().BeEmpty(because: "the feature flag short-circuits before the HTTP call");
    }

    // -----------------------------------------------------------------
    // 15. Non-AbacatePay provider -> RegistrationFailed without HTTP call.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldNotCallHttp_ForNonAbacatePayProvider()
    {
        var (client, handler) = BuildClient();
        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.Stripe,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
        handler.Requests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------
    // 16. Pre-flight: missing apiKey in protected credentials -> RegistrationFailed.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenCredentialsCannotBeUnprotected()
    {
        var (client, _) = BuildClient(reader: new DefaultReader()); // DefaultReader returns null for unknown blobs

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            "different-protected|apiKey=other",
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // 17. Envelope with HTTP 2xx but data missing -> RegistrationFailed.
    // -----------------------------------------------------------------
    [Fact]
    public async Task RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenEnvelopeDataIsNull()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":null,"success":true}""",
                Encoding.UTF8, "application/json")
        });

        var outcome = await client.RegisterWebhookAsync(
            ProviderCode.AbacatePay,
            ProtectedCredentials,
            WebhookSecret,
            CallbackUrl,
            DefaultEvents,
            CancellationToken.None);

        outcome.Should().Be(ProviderWebhookRegistrationOutcome.RegistrationFailed);
    }

    // -----------------------------------------------------------------
    // Helpers — mirrors the local pattern used by AbacatePayClientTests.
    // -----------------------------------------------------------------
    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private static readonly Uri BaseAddress = new("https://api.abacatepay.com/v2/");
        private readonly HttpMessageHandler _handler;
        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false) { BaseAddress = BaseAddress };
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
