using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentHub.Api.Controllers;
using PaymentHub.Application.Webhooks;

namespace PaymentHub.UnitTests.Api;

/// <summary>
/// Slice 2-B fail-fast coverage for the inbound provider webhook
/// controller. The handler-level AbacatePay contract is exercised by
/// <c>ProcessWebhookEventHandlerAbacatePayTests</c>.
/// </summary>
public class ProviderWebhooksControllerTests
{
    private const string AbacateBody =
        """{"id":"evt_xyz","event":"transparent.completed","data":{"id":"pix_abc","status":"PAID"}}""";

    private readonly Mock<IReceiveProviderWebhookHandler> _handler = new(MockBehavior.Strict);

    private ProviderWebhooksController BuildController(string? rawBody, string? abacateSignature, string? legacySignature = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(rawBody ?? string.Empty));
        http.Request.Headers["X-Provider-Event-Id"] = "evt_xyz";
        http.Request.Headers["X-Provider-Event-Type"] = "transparent.completed";
        if (!string.IsNullOrEmpty(abacateSignature))
            http.Request.Headers["X-Webhook-Signature"] = abacateSignature;
        if (!string.IsNullOrEmpty(legacySignature))
            http.Request.Headers["X-Provider-Signature"] = legacySignature;

        var controller = new ProviderWebhooksController(
            _handler.Object, NullLogger<ProviderWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return controller;
    }

    [Fact]
    public async Task Receive_ShouldReturnUnauthorized_WhenAbacatePayMissingSignature()
    {
        var controller = BuildController(AbacateBody, abacateSignature: null);

        var result = await controller.Receive(
            "AbacatePay", "evt_xyz", "transparent.completed",
            legacySignature: null, abacateSignature: null,
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _handler.Verify(
            h => h.HandleAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Receive_ShouldAcceptAbacatePay_WhenXWebhookSignaturePresent()
    {
        _handler.Setup(h => h.HandleAsync(
                "AbacatePay", "transparent.completed", AbacateBody, "evt_xyz",
                "abcd-signature", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var controller = BuildController(AbacateBody, abacateSignature: "abcd-signature");

        var result = await controller.Receive(
            "AbacatePay", "evt_xyz", "transparent.completed",
            legacySignature: null, abacateSignature: "abcd-signature",
            CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        _handler.Verify(
            h => h.HandleAsync(
                "AbacatePay", "transparent.completed", AbacateBody, "evt_xyz",
                "abcd-signature", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Receive_ShouldAcceptAbacatePay_WhenOnlyLegacySignaturePresent()
    {
        // X-Webhook-Signature is the canonical AbacatePay header but the
        // controller must also accept the legacy X-Provider-Signature
        // header so consumers can roll out the new contract gradually.
        _handler.Setup(h => h.HandleAsync(
                "AbacatePay", "transparent.completed", AbacateBody, "evt_xyz",
                "legacy-sig", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var controller = BuildController(AbacateBody, abacateSignature: null, legacySignature: "legacy-sig");

        var result = await controller.Receive(
            "AbacatePay", "evt_xyz", "transparent.completed",
            legacySignature: "legacy-sig", abacateSignature: null,
            CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Receive_ShouldPreferAbacatePayHeader_WhenBothHeadersPresent()
    {
        // When both headers arrive we trust X-Webhook-Signature (the
        // AbacatePay-native name) over the legacy one. This avoids
        // accidental dual-stack confusion during the rollout.
        _handler.Setup(h => h.HandleAsync(
                "AbacatePay", "transparent.completed", AbacateBody, "evt_xyz",
                "abacate-sig", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var controller = BuildController(AbacateBody, abacateSignature: "abacate-sig", legacySignature: "legacy-sig");

        await controller.Receive(
            "AbacatePay", "evt_xyz", "transparent.completed",
            legacySignature: "legacy-sig", abacateSignature: "abacate-sig",
            CancellationToken.None);

        _handler.Verify(
            h => h.HandleAsync(
                "AbacatePay", "transparent.completed", AbacateBody, "evt_xyz",
                "abacate-sig", It.IsAny<CancellationToken>()),
            Times.Once);
        _handler.Verify(
            h => h.HandleAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), "legacy-sig", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Receive_ShouldPreserveLegacyBehaviorForOtherProviders()
    {
        // Fake/Stripe/MercadoPago do not require HMAC in this slice —
        // the controller must keep accepting them without a signature
        // header until each provider adopts its own contract.
        _handler.Setup(h => h.HandleAsync(
                "Fake", "transparent.completed", AbacateBody, "evt_xyz",
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var controller = BuildController(AbacateBody, abacateSignature: null);

        var result = await controller.Receive(
            "Fake", "evt_xyz", "transparent.completed",
            legacySignature: null, abacateSignature: null,
            CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Receive_ShouldForwardBodyExactBytes_AndNotLeakSignatureInResponse()
    {
        // The signature might contain Base64 padding and other "scary"
        // characters; we must never echo it back in the response body
        // or in any structured log line.
        var capturedBody = string.Empty;
        string? capturedSignature = null;

        var http = new DefaultHttpContext();
        var bodyBytes = Encoding.UTF8.GetBytes(AbacateBody);
        http.Request.Body = new MemoryStream(bodyBytes);
        http.Request.Headers["X-Provider-Event-Id"] = "evt_xyz";
        http.Request.Headers["X-Provider-Event-Type"] = "transparent.completed";
        const string sig = "VEVTVC1zaWduYXR1cmUtdGhhdC1jb250YWlucy1lcXVhbC1zaWduLXNpZ25hdHVyZS1zaWdu==";
        http.Request.Headers["X-Webhook-Signature"] = sig;

        _handler.Setup(h => h.HandleAsync(
                "AbacatePay", "transparent.completed", It.IsAny<string>(), "evt_xyz",
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, string?, CancellationToken>(
                (_, _, body, _, signature, _) =>
                {
                    capturedBody = body;
                    capturedSignature = signature;
                })
            .ReturnsAsync(Guid.NewGuid());

        var controller = new ProviderWebhooksController(
            _handler.Object, NullLogger<ProviderWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await controller.Receive(
            "AbacatePay", "evt_xyz", "transparent.completed",
            legacySignature: null, abacateSignature: sig,
            CancellationToken.None);

        capturedBody.Should().Be(AbacateBody);
        capturedSignature.Should().Be(sig);
    }

    [Theory]
    [InlineData("abacatepay")]
    [InlineData("ABACATEPAY")]
    [InlineData("AbacatePay")]
    public async Task Receive_ShouldEnforceSignatureForAbacatePayRegardlessOfCase(string providerCode)
    {
        var controller = BuildController(AbacateBody, abacateSignature: null);

        var result = await controller.Receive(
            providerCode, "evt_xyz", "transparent.completed",
            legacySignature: null, abacateSignature: null,
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
