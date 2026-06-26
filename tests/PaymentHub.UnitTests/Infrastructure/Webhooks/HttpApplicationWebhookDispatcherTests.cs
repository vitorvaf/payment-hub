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

    private static HttpApplicationWebhookDispatcher CreateDispatcher(
        IApplicationClientRepository appsRepo,
        CapturingHandler handler,
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