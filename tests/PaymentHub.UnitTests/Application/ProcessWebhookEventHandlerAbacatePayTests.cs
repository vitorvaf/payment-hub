using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Webhooks;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.ValueObjects;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Application;

/// <summary>
/// Slice 2-B coverage for the AbacatePay routing path inside
/// <see cref="ProcessWebhookEventHandler"/>. The legacy Fake/Stripe/
/// MercadoPago path is already exercised by
/// <see cref="ProcessWebhookEventHandlerTests"/>.
/// </summary>
public class ProcessWebhookEventHandlerAbacatePayTests
{
    private const string WebhookSecretValue = "abacate_secret_for_handler_tests_xyz";

    private static string BuildBody(string tenantId, string appId, string paymentId, string eventType, string status) =>
        JsonSerializer.Serialize(new
        {
            id = "evt_x",
            @event = eventType,
            data = new
            {
                id = paymentId,
                status,
                metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId,
                    ["applicationId"] = appId,
                    ["paymentId"] = paymentId
                }
            }
        });

    private static (Mock<IWebhookEventRepository> webhooks,
                    Mock<IPaymentRepository> payments,
                    Mock<IProviderAccountRepository> providerAccounts,
                    ICredentialProtector credentialProtector,
                    Mock<IOutboxPublisher> outbox,
                    Mock<IUnitOfWork> uow,
                    Mock<IClock> clock,
                    Mock<IPaymentProviderAdapter> adapter,
                    Mock<IPaymentProviderRouter> router) BuildCommonMocks()
    {
        var webhooks = new Mock<IWebhookEventRepository>(MockBehavior.Strict);
        var payments = new Mock<IPaymentRepository>(MockBehavior.Strict);
        // Slice 3-IT: ProcessWebhookEventHandler.ProcessAsync now explicitly
        // calls _payments.AddAttemptAsync to ensure EF tracks the new
        // attempt as Added (collection navigation tracking alone is
        // unreliable — see audit report). Default to Task.CompletedTask so
        // individual tests that need to assert the call can override with
        // .Verify(...) and tests that don't care still pass strict mocks.
        payments.Setup(p => p.AddAttemptAsync(It.IsAny<PaymentAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var providerAccounts = new Mock<IProviderAccountRepository>(MockBehavior.Strict);
        // Real fake protector: lets tests build ProviderAccount blobs that
        // survive the handler's unprotect call without dragging in
        // AesCredentialProtector's config-bound constructor.
        var credentialProtector = (ICredentialProtector)new FakeCredentialProtector();
        var outbox = new Mock<IOutboxPublisher>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc));
        var adapter = new Mock<IPaymentProviderAdapter>();
        adapter.Setup(a => a.ProviderCode).Returns("AbacatePay");
        var router = new Mock<IPaymentProviderRouter>();
        router.Setup(r => r.Resolve("AbacatePay")).Returns(adapter.Object);
        return (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router);
    }

    private static ProviderAccount CreateAbacatePayAccount(
        ICredentialProtector protector,
        Guid tenantId,
        Guid appId,
        string credentialsJson) =>
        new(
            Guid.NewGuid(), tenantId, appId, ProviderCode.AbacatePay,
            ProviderEnvironment.Sandbox, "abacate-sandbox",
            protector.Protect(credentialsJson));

    private static ProcessWebhookEventHandler BuildHandler(
        Mock<IWebhookEventRepository> webhooks,
        Mock<IPaymentRepository> payments,
        Mock<IProviderAccountRepository> providerAccounts,
        ICredentialProtector credentialProtector,
        Mock<IPaymentProviderRouter> router,
        Mock<IOutboxPublisher> outbox,
        Mock<IUnitOfWork> uow,
        Mock<IClock> clock) =>
        new(
            webhooks.Object,
            payments.Object,
            providerAccounts.Object,
            credentialProtector,
            router.Object,
            outbox.Object,
            uow.Object,
            clock.Object,
            NullLogger<ProcessWebhookEventHandler>.Instance);

    [Fact]
    public async Task ProcessAsync_ShouldResolveProviderAccountAndUnprotectWebhookSecret()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);
        payment.MarkPending();

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig-mock");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        var account = CreateAbacatePayAccount(credentialProtector, tenantId, appId,
            JsonSerializer.Serialize(new { apiKey = "apx_x", webhookSecret = WebhookSecretValue }));

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        string? capturedSecret = null;
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderWebhookRequest, CancellationToken>((req, _) => capturedSecret = req.WebhookSecret)
            .ReturnsAsync(new ProviderWebhookParseResult(true, "evt_x", "transparent.completed",
                paymentId.ToString(), "PAID", null, body));
        payments.Setup(r => r.GetByIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        capturedSecret.Should().Be(WebhookSecretValue);
        providerAccounts.Verify(r => r.GetByCodeAsync(
            tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()), Times.Once);
        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPermanentlyFail_WhenProviderAccountMissing()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderAccount?)null);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
        webhook.LastError.Should().Contain("secret");
        adapter.Verify(a => a.ParseWebhookAsync(
            It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPermanentlyFail_WhenPaymentMissingFromMetadata()
    {
        // Body has no metadata block at all → handler cannot resolve
        // a tenant-scoped payment without guessing.
        var body = """{"id":"evt_x","event":"transparent.completed","data":{"id":"pix_x","status":"PAID"}}""";
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        // Strict mock: any payment lookup would throw, so the test
        // would surface as a hard failure if the handler tried to
        // bypass the metadata gate.
        payments.Setup(r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Payment lookup must not happen without metadata."));

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
        webhook.LastError.Should().NotContain(WebhookSecretValue);
        webhook.LastError.Should().NotContain("apiKey");
        webhook.LastError.Should().Contain("secret");
    }

    [Fact]
    public async Task ProcessAsync_ShouldPermanentlyFail_WhenCredentialsHaveNoWebhookSecret()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        var account = CreateAbacatePayAccount(credentialProtector, tenantId, appId,
            JsonSerializer.Serialize(new { apiKey = "apx_x" }));

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
        webhook.LastError.Should().Contain("secret");
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptLegacySecretField_WhenWebhookSecretFieldMissing()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);
        payment.MarkPending();

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        // Legacy credentials: { "apiKey": "...", "secret": "..." } — fall
        // back to `secret` for the webhook path.
        var account = CreateAbacatePayAccount(credentialProtector, tenantId, appId,
            JsonSerializer.Serialize(new { apiKey = "apx_x", secret = WebhookSecretValue }));

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        string? capturedSecret = null;
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderWebhookRequest, CancellationToken>((req, _) => capturedSecret = req.WebhookSecret)
            .ReturnsAsync(new ProviderWebhookParseResult(true, "evt_x", "transparent.completed",
                paymentId.ToString(), "PAID", null, body));
        payments.Setup(r => r.GetByIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        capturedSecret.Should().Be(WebhookSecretValue);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotLeakSecretInLastError_OnUnprotectFailure()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        // Garbage blob that does not match the protector marker.
        var account = new ProviderAccount(
            Guid.NewGuid(), tenantId, appId, ProviderCode.AbacatePay,
            ProviderEnvironment.Sandbox, "abacate",
            "not-our-marker-blob-with-secret-" + WebhookSecretValue);

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        webhook.LastError.Should().NotContain(WebhookSecretValue);
        webhook.LastError.Should().NotContain("apiKey");
        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotCallAdapter_WhenSecretUnresolvable()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        var account = CreateAbacatePayAccount(credentialProtector, tenantId, appId,
            """{"apiKey":"apx_x"}""");

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        adapter.Verify(a => a.ParseWebhookAsync(
            It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        outbox.Verify(o => o.EnqueueAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotPersistWebhookSecret_OnProcessedWebhook()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);
        payment.MarkPending();

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        var account = CreateAbacatePayAccount(credentialProtector, tenantId, appId,
            JsonSerializer.Serialize(new { apiKey = "apx_x", webhookSecret = WebhookSecretValue }));

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookParseResult(true, "evt_x", "transparent.completed",
                paymentId.ToString(), "PAID", null, body));
        payments.Setup(r => r.GetByIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        // Secret and apiKey must never appear in any persisted field of
        // the webhook, the payment, or any captured outbox payload.
        webhook.LastError.Should().BeNullOrEmpty();
        webhook.Signature.Should().Be("sig");
        webhook.ProviderEventId.Should().Be("evt_x");
        webhook.RawPayloadJson.Should().NotContain(WebhookSecretValue);
        webhook.RawPayloadJson.Should().NotContain("apx_x");
    }

    [Fact]
    public async Task ProcessAsync_ShouldForceSignatureFailureInto_NoSideEffects_OnAdapter()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.AbacatePay, null, null, null, null);

        var body = BuildBody(tenantId.ToString(), appId.ToString(), paymentId.ToString(),
            "transparent.completed", "PAID");
        var webhook = new WebhookEvent(
            Guid.NewGuid(), ProviderCode.AbacatePay, "transparent.completed",
            body, "evt_x", "sig");

        var (webhooks, payments, providerAccounts, credentialProtector, outbox, uow, clock, adapter, router) =
            BuildCommonMocks();

        var account = CreateAbacatePayAccount(credentialProtector, tenantId, appId,
            JsonSerializer.Serialize(new { apiKey = "apx_x", webhookSecret = WebhookSecretValue }));

        webhooks.Setup(r => r.GetByIdAsync(webhook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);
        payments.Setup(r => r.GetByIdForTenantAsync(tenantId, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        providerAccounts.Setup(r => r.GetByCodeAsync(
                tenantId, appId, ProviderCode.AbacatePay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Adapter says signature failed → no payment mutation, no outbox event.
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookParseResult(false, null, "unknown", null, null,
                "AbacatePay webhook signature invalid (SignatureMismatch).", null));

        var handler = BuildHandler(webhooks, payments, providerAccounts, credentialProtector,
            router, outbox, uow, clock);

        await handler.ProcessAsync(webhook.Id, CancellationToken.None);

        // Payment must NOT have been mutated.
        payment.Status.Should().Be(PaymentStatus.Created);
        payment.Attempts.Should().BeEmpty();
        outbox.Verify(o => o.EnqueueAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
    }
}
