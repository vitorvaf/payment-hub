using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.Infrastructure.Postgres.Webhooks;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Infrastructure.Webhooks;

public class HttpApplicationWebhookDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ShouldUnprotectWebhookSecret_BeforeSigningRequest()
    {
        var rawSecret = "raw-webhook-secret-plain-text";
        var protectedSecret = new FakeWebhookSecretProtector().Protect(rawSecret);

        var app = new ApplicationClient(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TestApp",
            webhookUrl: "https://example.invalid/hook",
            protectedWebhookSecret: protectedSecret);

        var outbox = new OutboxEvent(
            Guid.NewGuid(),
            app.TenantId,
            app.Id,
            "payment.approved",
            "{\"eventId\":\"00000000-0000-0000-0000-000000000001\"}");

        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(app.TenantId, app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var captured = new CapturingHandler();
        var dispatcher = CreateDispatcher(appsRepo.Object, captured, new FakeWebhookSecretProtector());

        await dispatcher.DispatchAsync(outbox, CancellationToken.None);

        captured.Request.Should().NotBeNull();
        captured.Request!.Headers.TryGetValues("X-PaymentHub-Signature", out var signatures).Should().BeTrue();
        var signature = signatures!.Single();

        // Verify signature matches the raw secret using the public signer.
        var signer = new PaymentHub.Infrastructure.Postgres.Security.HmacWebhookSigner();
        captured.Timestamp.Should().NotBeNullOrEmpty();
        signer.Verify(outbox.PayloadJson, rawSecret, captured.Timestamp!, signature).Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_ShouldNotIncludeSignature_WhenApplicationHasNoWebhookSecret()
    {
        var app = new ApplicationClient(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TestApp",
            webhookUrl: "https://example.invalid/hook",
            protectedWebhookSecret: null);

        var outbox = new OutboxEvent(
            Guid.NewGuid(),
            app.TenantId,
            app.Id,
            "payment.approved",
            "{}");

        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(app.TenantId, app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var captured = new CapturingHandler();
        var dispatcher = CreateDispatcher(appsRepo.Object, captured, new FakeWebhookSecretProtector());

        await dispatcher.DispatchAsync(outbox, CancellationToken.None);

        captured.Request.Should().NotBeNull();
        captured.Request!.Headers.TryGetValues("X-PaymentHub-Signature", out var _).Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_ShouldNotSendRequest_WhenProtectedSecretCannotBeUnprotected()
    {
        // Create an invalid protected secret: not produced by FakeWebhookSecretProtector.
        var app = new ApplicationClient(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TestApp",
            webhookUrl: "https://example.invalid/hook",
            protectedWebhookSecret: Convert.ToBase64String(new byte[32]));

        var outbox = new OutboxEvent(
            Guid.NewGuid(),
            app.TenantId,
            app.Id,
            "payment.approved",
            "{}");

        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(app.TenantId, app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var captured = new CapturingHandler();
        var dispatcher = CreateDispatcher(appsRepo.Object, captured, new FakeWebhookSecretProtector());

        // Slice 7-A.7: unprotect failure now raises a typed WebhookDispatcherException so the
        // worker can categorise the failure without persisting the exception's message.
        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        await act.Should().ThrowAsync<WebhookDispatcherException>()
            .Where(ex => ex.Category == WebhookDispatcherCategory.UnprotectFailure);

        // Dispatcher must NOT send the HTTP request when the protected secret cannot be unprotectd,
        // because sending without a valid signature would either deliver a wrong signature or expose
        // a half-protected secret through the HMAC pipeline.
        captured.Request.Should().BeNull("dispatcher must skip the HTTP request when secret cannot be unprotected");
    }

    // =================================================================================================
    // Slice 7-A.8 — strong coverage using ScriptedHandler. Covers HTTP 4xx/5xx, network/timeout
    // errors, missing-webhook-url paths and the security invariants (no body / no URL leakage).
    // =================================================================================================

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DispatchAsync_ShouldThrowHttpFailureWithStatusCode_WhenConsumerReturnsNon2xx(HttpStatusCode statusCode)
    {
        var (dispatcher, _, _) = BuildDispatcher(_ => _
            .Enqueue(statusCode));

        var outbox = NewOutboxEvent("payment.approved");

        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        var assertion = await act.Should().ThrowAsync<WebhookDispatcherException>();
        assertion.Which.Category.Should().Be(WebhookDispatcherCategory.HttpFailure);
        assertion.Which.StatusCode.Should().Be((int)statusCode);
    }

    [Fact]
    public async Task DispatchAsync_ShouldThrowNetworkError_WhenHttpRequestExceptionThrown()
    {
        var (dispatcher, _, _) = BuildDispatcher(handler =>
            handler.EnqueueThrow(new HttpRequestException("connection refused")));

        var outbox = NewOutboxEvent("payment.approved");

        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        var assertion = await act.Should().ThrowAsync<WebhookDispatcherException>();
        assertion.Which.Category.Should().Be(WebhookDispatcherCategory.NetworkError);
        assertion.Which.StatusCode.Should().BeNull();
        assertion.Which.Message.Should().NotContain("connection refused",
            "raw exception messages must never leak into typed exceptions");
    }

    [Fact]
    public async Task DispatchAsync_ShouldThrowTimeout_WhenHandlerSimulatesHttpClientTimeout()
    {
        // HttpClient surfaces its Timeout as TaskCanceledException with no inner
        // OperationCanceledException — mirror that here so the dispatcher's catch sees the same
        // signal it would in production.
        var (dispatcher, _, _) = BuildDispatcher(handler =>
            handler.EnqueueThrow(new TaskCanceledException("HttpClient.Timeout fired")));

        var outbox = NewOutboxEvent("payment.approved");

        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        var assertion = await act.Should().ThrowAsync<WebhookDispatcherException>();
        assertion.Which.Category.Should().Be(WebhookDispatcherCategory.Timeout);
        assertion.Which.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_ShouldThrowMissingWebhookUrl_WhenApplicationNotFoundUnderTenant()
    {
        // Tenant guard (B1): an OutboxEvent referencing a (tenantId, applicationId) that the
        // repository cannot resolve must NOT be silently dropped — the worker needs to know so
        // it can retry or fail according to policy instead of marking the event as Sent.
        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationClient?)null);

        var handler = new ScriptedHandler(); // empty queue — must never be hit
        var dispatcher = new HttpApplicationWebhookDispatcher(
            new SingleHandlerHttpClientFactory(handler),
            appsRepo.Object,
            new PaymentHub.Infrastructure.Postgres.Security.HmacWebhookSigner(),
            new FakeWebhookSecretProtector(),
            NullLogger<HttpApplicationWebhookDispatcher>.Instance,
            Options.Create(new PaymentHubOptions { WebhookHttpTimeoutSeconds = 10 }));

        var outbox = NewOutboxEvent("payment.approved");

        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        var assertion = await act.Should().ThrowAsync<WebhookDispatcherException>();
        assertion.Which.Category.Should().Be(WebhookDispatcherCategory.MissingWebhookUrl);
        assertion.Which.StatusCode.Should().BeNull();
        handler.Requests.Should().BeEmpty("dispatcher must NOT send HTTP when application is missing");
    }

    [Fact]
    public async Task DispatchAsync_ShouldThrowMissingWebhookUrl_WhenWebhookUrlIsBlank()
    {
        var app = new ApplicationClient(
            Guid.NewGuid(), Guid.NewGuid(), "TestApp",
            webhookUrl: "   ", protectedWebhookSecret: null);

        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var handler = new ScriptedHandler();
        var dispatcher = new HttpApplicationWebhookDispatcher(
            new SingleHandlerHttpClientFactory(handler),
            appsRepo.Object,
            new PaymentHub.Infrastructure.Postgres.Security.HmacWebhookSigner(),
            new FakeWebhookSecretProtector(),
            NullLogger<HttpApplicationWebhookDispatcher>.Instance,
            Options.Create(new PaymentHubOptions { WebhookHttpTimeoutSeconds = 10 }));

        var outbox = NewOutboxEvent("payment.approved");

        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        var assertion = await act.Should().ThrowAsync<WebhookDispatcherException>();
        assertion.Which.Category.Should().Be(WebhookDispatcherCategory.MissingWebhookUrl);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_ShouldNotIncludeResponseBody_InExceptionMessage()
    {
        // M2-security: the consumer's response body is consumer-controlled and may contain
        // sensitive material. The exception message MUST NOT contain it because the worker
        // would otherwise persist it into OutboxEvent.LastError.
        const string sensitiveBody = "{\"error\":\"internal\",\"stack\":\"secret-stack-trace\"}";

        var (dispatcher, handler, _) = BuildDispatcher(h =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(sensitiveBody, System.Text.Encoding.UTF8, "application/json")
            };
            h.Enqueue(_ => response);
        });

        var outbox = NewOutboxEvent("payment.approved");

        var act = async () => await dispatcher.DispatchAsync(outbox, CancellationToken.None);
        var assertion = await act.Should().ThrowAsync<WebhookDispatcherException>();
        assertion.Which.Message.Should().NotContain(sensitiveBody);
        assertion.Which.Message.Should().NotContain("secret-stack-trace");
        // Ensure the consumer actually received the body we sent — if the test wires the wrong
        // content, the assertion above would pass by accident.
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task DispatchAsync_ShouldIncludeAllPaymentHubHeaders_OnSuccess()
    {
        var app = new ApplicationClient(
            Guid.NewGuid(), Guid.NewGuid(), "TestApp",
            webhookUrl: "https://example.invalid/hook",
            protectedWebhookSecret: new FakeWebhookSecretProtector().Protect("any-raw-secret"));

        var outbox = NewOutboxEvent("payment.approved");

        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var handler = new ScriptedHandler().Enqueue(HttpStatusCode.OK);
        var dispatcher = new HttpApplicationWebhookDispatcher(
            new SingleHandlerHttpClientFactory(handler),
            appsRepo.Object,
            new PaymentHub.Infrastructure.Postgres.Security.HmacWebhookSigner(),
            new FakeWebhookSecretProtector(),
            NullLogger<HttpApplicationWebhookDispatcher>.Instance,
            Options.Create(new PaymentHubOptions { WebhookHttpTimeoutSeconds = 10 }));

        await dispatcher.DispatchAsync(outbox, CancellationToken.None);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Headers.GetValues("X-PaymentHub-Event-Id").Single().Should().Be(outbox.Id.ToString());
        sent.Headers.GetValues("X-PaymentHub-Event-Type").Single().Should().Be("payment.approved");
        sent.Headers.GetValues("X-PaymentHub-Timestamp").Single().Should().NotBeNullOrEmpty();
        sent.Headers.GetValues("X-PaymentHub-Signature").Single().Should().NotBeNullOrEmpty();
        handler.CapturedBodies.Should().ContainSingle()
            .Which.Should().Be(outbox.PayloadJson);
    }

    // --- helpers ---

    private static OutboxEvent NewOutboxEvent(string eventType)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), eventType, "{}");

    private static (HttpApplicationWebhookDispatcher dispatcher, ScriptedHandler handler, Mock<IApplicationClientRepository> repo) BuildDispatcher(
        Action<ScriptedHandler> configure)
    {
        var app = new ApplicationClient(
            Guid.NewGuid(), Guid.NewGuid(), "TestApp",
            webhookUrl: "https://example.invalid/hook",
            protectedWebhookSecret: null);

        var appsRepo = new Mock<IApplicationClientRepository>(MockBehavior.Strict);
        appsRepo.Setup(r => r.GetByTenantAndIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var handler = new ScriptedHandler();
        configure(handler);

        var dispatcher = new HttpApplicationWebhookDispatcher(
            new SingleHandlerHttpClientFactory(handler),
            appsRepo.Object,
            new PaymentHub.Infrastructure.Postgres.Security.HmacWebhookSigner(),
            new FakeWebhookSecretProtector(),
            NullLogger<HttpApplicationWebhookDispatcher>.Instance,
            Options.Create(new PaymentHubOptions { WebhookHttpTimeoutSeconds = 10 }));

        return (dispatcher, handler, appsRepo);
    }

    private static HttpApplicationWebhookDispatcher CreateDispatcher(
        IApplicationClientRepository appsRepo,
        HttpMessageHandler handler,
        IWebhookSecretProtector protector)
    {
        var factory = new SingleHandlerHttpClientFactory(handler);

        return new HttpApplicationWebhookDispatcher(
            factory,
            appsRepo,
            new PaymentHub.Infrastructure.Postgres.Security.HmacWebhookSigner(),
            protector,
            NullLogger<HttpApplicationWebhookDispatcher>.Instance,
            Options.Create(new PaymentHubOptions { WebhookHttpTimeoutSeconds = 10 }));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Timestamp { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Headers.TryGetValues("X-PaymentHub-Timestamp", out var timestamps))
                Timestamp = timestamps.Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
        {
            // Bypass the named-client registration entirely; we are providing a test-only
            // factory that produces a client whose request travels through our capturing handler.
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}