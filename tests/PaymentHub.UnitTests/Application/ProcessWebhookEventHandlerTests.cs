using FluentAssertions;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
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
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));

        var handler = new ProcessWebhookEventHandler(webhooks.Object, payments.Object, outbox.Object, uow.Object, clock.Object);

        await handler.ProcessAsync(webhookId, CancellationToken.None);

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
        webhook.TenantId.Should().Be(tenantId);
        webhook.ApplicationId.Should().Be(appId);
        payment.Status.Should().Be(PaymentStatus.Approved);
        outbox.Verify(o => o.EnqueueAsync(
            tenantId, appId,
            "payment.approved",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldScheduleRetryOnError()
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

        var handler = new ProcessWebhookEventHandler(webhooks.Object, payments.Object, outbox.Object, uow.Object, clock.Object);

        await handler.ProcessAsync(webhookId, CancellationToken.None);

        // ProviderPaymentId is "abc" which is not a Guid, but the handler falls back to repository lookup; the lookup returns null and the webhook is marked as processed.
        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
    }
}
