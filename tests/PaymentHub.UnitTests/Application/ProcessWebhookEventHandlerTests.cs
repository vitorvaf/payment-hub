using FluentAssertions;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Webhooks;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.ValueObjects;

namespace PaymentHub.UnitTests.Application;

public class ProcessWebhookEventHandlerTests
{
    [Fact]
    public async Task ProcessAsync_ShouldMarkProcessedAndEmitOutboxOnStatusChange()
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();
        var webhook = new WebhookEvent(
            webhookId,
            ProviderCode.Fake,
            "payment.approved",
            $"{{\"id\":\"{paymentId}\",\"status\":\"approved\"}}",
            "evt-1",
            null);

        var webhooks = new Mock<IWebhookEventRepository>();
        webhooks.Setup(r => r.GetByIdAsync(webhookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.Fake, null, null, null, null);
        payment.MarkPending();

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.GetByIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        payments.Setup(r => r.GetByProviderPaymentIdAsync("Fake", paymentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var outbox = new Mock<IOutboxPublisher>();
        outbox.Setup(o => o.EnqueueAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));
        var adapter = new Mock<IPaymentProviderAdapter>();
        adapter.Setup(a => a.ProviderCode).Returns("Fake");
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookParseResult(
                true,
                "evt-1",
                "payment.approved",
                paymentId.ToString(),
                "approved",
                null,
                null));
        var router = new Mock<IPaymentProviderRouter>();
        router.Setup(r => r.Resolve("Fake")).Returns(adapter.Object);

        var handler = new ProcessWebhookEventHandler(
            webhooks.Object, payments.Object, router.Object, outbox.Object, uow.Object, clock.Object);

        await handler.ProcessAsync(webhookId, CancellationToken.None);

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
        webhook.TenantId.Should().Be(tenantId);
        webhook.ApplicationId.Should().Be(appId);
        payment.Status.Should().Be(PaymentStatus.Approved);
        payment.Attempts.Should().ContainSingle(a => a.Status == PaymentAttemptStatus.Succeeded);
        outbox.Verify(o => o.EnqueueAsync(
            It.IsAny<Guid>(),
            tenantId, appId,
            "payment.approved",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
        adapter.Verify(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPermanentlyFailWhenPaymentDoesNotExist()
    {
        var webhookId = Guid.NewGuid();
        var webhook = new WebhookEvent(
            webhookId,
            ProviderCode.Fake,
            "payment.approved",
            "{\"id\":\"abc\",\"status\":\"approved\"}",
            "evt-2",
            null);

        var webhooks = new Mock<IWebhookEventRepository>();
        webhooks.Setup(r => r.GetByIdAsync(webhookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);
        payments.Setup(r => r.GetByProviderPaymentIdAsync("Fake", "abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        var outbox = new Mock<IOutboxPublisher>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));
        var adapter = new Mock<IPaymentProviderAdapter>();
        adapter.Setup(a => a.ProviderCode).Returns("Fake");
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookParseResult(
                true,
                "evt-2",
                "payment.approved",
                "abc",
                "approved",
                null,
                null));
        var router = new Mock<IPaymentProviderRouter>();
        router.Setup(r => r.Resolve("Fake")).Returns(adapter.Object);

        var handler = new ProcessWebhookEventHandler(
            webhooks.Object, payments.Object, router.Object, outbox.Object, uow.Object, clock.Object);

        await handler.ProcessAsync(webhookId, CancellationToken.None);

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
        webhook.LastError.Should().Contain("Payment not found");
        outbox.Verify(o => o.EnqueueAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rejected", PaymentStatus.Rejected)]
    [InlineData("failed", PaymentStatus.Failed)]
    [InlineData("expired", PaymentStatus.Expired)]
    [InlineData("cancelled", PaymentStatus.Cancelled)]
    public async Task ProcessAsync_ShouldRegisterFailedAttemptForNegativePaymentStatuses(
        string providerStatus,
        PaymentStatus expectedStatus)
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();
        var webhook = new WebhookEvent(
            webhookId,
            ProviderCode.Fake,
            $"payment.{providerStatus}",
            $"{{\"id\":\"{paymentId}\",\"status\":\"{providerStatus}\"}}",
            $"evt-{providerStatus}",
            null);

        var webhooks = new Mock<IWebhookEventRepository>();
        webhooks.Setup(r => r.GetByIdAsync(webhookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.Fake, null, null, null, null);
        payment.MarkPending();

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.GetByIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var outbox = new Mock<IOutboxPublisher>();
        outbox.Setup(o => o.EnqueueAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));

        var adapter = new Mock<IPaymentProviderAdapter>();
        adapter.Setup(a => a.ProviderCode).Returns("Fake");
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookParseResult(
                true,
                $"evt-{providerStatus}",
                $"payment.{providerStatus}",
                paymentId.ToString(),
                providerStatus,
                null,
                null));

        var router = new Mock<IPaymentProviderRouter>();
        router.Setup(r => r.Resolve("Fake")).Returns(adapter.Object);

        var handler = new ProcessWebhookEventHandler(
            webhooks.Object, payments.Object, router.Object, outbox.Object, uow.Object, clock.Object);

        await handler.ProcessAsync(webhookId, CancellationToken.None);

        payment.Status.Should().Be(expectedStatus);
        payment.Attempts.Should().ContainSingle(a => a.Status == PaymentAttemptStatus.Failed);
    }

    [Theory]
    [InlineData("pending", PaymentStatus.Pending)]
    [InlineData("processing", PaymentStatus.Processing)]
    [InlineData("requires_action", PaymentStatus.RequiresAction)]
    public async Task ProcessAsync_ShouldRegisterPendingAttemptForIntermediatePaymentStatuses(
        string providerStatus,
        PaymentStatus expectedStatus)
    {
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();
        var webhook = new WebhookEvent(
            webhookId,
            ProviderCode.Fake,
            $"payment.{providerStatus}",
            $"{{\"id\":\"{paymentId}\",\"status\":\"{providerStatus}\"}}",
            $"evt-{providerStatus}",
            null);

        var webhooks = new Mock<IWebhookEventRepository>();
        webhooks.Setup(r => r.GetByIdAsync(webhookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhook);

        var payment = new Payment(
            paymentId, tenantId, appId, "order-1",
            Money.Of(2990, "BRL"), ProviderCode.Fake, null, null, null, null);
        payment.MarkPending();

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.GetByIdAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var outbox = new Mock<IOutboxPublisher>();
        outbox.Setup(o => o.EnqueueAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));

        var adapter = new Mock<IPaymentProviderAdapter>();
        adapter.Setup(a => a.ProviderCode).Returns("Fake");
        adapter.Setup(a => a.ParseWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookParseResult(
                true,
                $"evt-{providerStatus}",
                $"payment.{providerStatus}",
                paymentId.ToString(),
                providerStatus,
                null,
                null));

        var router = new Mock<IPaymentProviderRouter>();
        router.Setup(r => r.Resolve("Fake")).Returns(adapter.Object);

        var handler = new ProcessWebhookEventHandler(
            webhooks.Object, payments.Object, router.Object, outbox.Object, uow.Object, clock.Object);

        await handler.ProcessAsync(webhookId, CancellationToken.None);

        payment.Status.Should().Be(expectedStatus);
        payment.Attempts.Should().ContainSingle(a => a.Status == PaymentAttemptStatus.Pending);
    }
}
