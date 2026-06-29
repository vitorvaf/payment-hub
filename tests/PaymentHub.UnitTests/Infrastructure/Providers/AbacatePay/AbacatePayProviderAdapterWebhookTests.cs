using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Providers.AbacatePay;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Webhook-handling tests for <see cref="AbacatePayProviderAdapter"/>. The
/// pure create/checkout path is covered by
/// <see cref="AbacatePayProviderAdapterTests"/>.
/// </summary>
public class AbacatePayProviderAdapterWebhookTests
{
    private const string Secret = "abacate_unit_test_webhook_secret_xyz_2026";

    private readonly HmacAbacatePayWebhookSignatureVerifier _verifier = new();
    private readonly AbacatePayWebhookNormalizer _normalizer = new();

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static string Body(string eventType, string status, string providerPaymentId = "pix_abc", string eventId = "evt_1") =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @event = eventType,
            apiVersion = 2,
            devMode = true,
            data = new
            {
                id = providerPaymentId,
                status,
                amount = 2990,
                metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = Guid.NewGuid().ToString(),
                    ["applicationId"] = Guid.NewGuid().ToString(),
                    ["paymentId"] = Guid.NewGuid().ToString()
                }
            }
        });

    private static AbacatePayProviderAdapter BuildAdapter() =>
        new(
            new NoopAbacatePayClient(),
            new FakeCredentialProtector(),
            new HmacAbacatePayWebhookSignatureVerifier(),
            new AbacatePayWebhookNormalizer(),
            NullLogger<AbacatePayProviderAdapter>.Instance);

    private static ProviderWebhookRequest BuildRequest(
        string body,
        string secret,
        string? signature = null) =>
        new(body, signature ?? ComputeSignature(body, secret), new Dictionary<string, string>())
        {
            WebhookSecret = secret,
            ProviderAccountId = Guid.NewGuid()
        };

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnValid_OnTransparentCompleted()
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();

        var result = await adapter.ParseWebhookAsync(BuildRequest(body, Secret), CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.ProviderEventId.Should().Be("evt_1");
        result.ProviderPaymentId.Should().Be("pix_abc");
        result.ProviderStatus.Should().Be("PAID");
        result.EventType.Should().Be("transparent.completed");
        result.ErrorMessage.Should().BeNull();
        result.RawPayloadJson.Should().Be(body);
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnValid_OnTransparentRefunded()
    {
        var body = Body("transparent.refunded", "REFUNDED");
        var adapter = BuildAdapter();

        var result = await adapter.ParseWebhookAsync(BuildRequest(body, Secret), CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.EventType.Should().Be("transparent.refunded");
        result.ProviderStatus.Should().Be("REFUNDED");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnValid_OnTransparentDisputed()
    {
        var body = Body("transparent.disputed", "DISPUTED");
        var adapter = BuildAdapter();

        var result = await adapter.ParseWebhookAsync(BuildRequest(body, Secret), CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.EventType.Should().Be("transparent.disputed");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnValid_OnTransparentLost()
    {
        var body = Body("transparent.lost", "LOST");
        var adapter = BuildAdapter();

        var result = await adapter.ParseWebhookAsync(BuildRequest(body, Secret), CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.EventType.Should().Be("transparent.lost");
        result.ProviderStatus.Should().Be("LOST");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenSecretMissing()
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();
        var req = new ProviderWebhookRequest(body, ComputeSignature(body, Secret), new Dictionary<string, string>())
        {
            WebhookSecret = null,
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("secret");
        result.ErrorMessage.Should().NotContain(Secret);
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenSignatureHeaderMissing()
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();
        var req = new ProviderWebhookRequest(body, Signature: null, new Dictionary<string, string>())
        {
            WebhookSecret = Secret,
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("signature");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenSignatureDoesNotMatch()
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();
        // Sign the body with one secret but ask the adapter to verify with
        // a different one — that is what the worker would observe if the
        // ProviderAccount was misrouted.
        var signature = ComputeSignature(body, "signing-secret-a");
        var req = new ProviderWebhookRequest(body, signature, new Dictionary<string, string>())
        {
            WebhookSecret = "signing-secret-b",
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("signature");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenBodyTamperedAfterSigning()
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();
        var tampered = body + " ";
        var sig = ComputeSignature(body, Secret); // valid for original body only
        var req = new ProviderWebhookRequest(tampered, sig, new Dictionary<string, string>())
        {
            WebhookSecret = Secret,
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("signature");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenSignatureIsNotBase64()
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();
        var req = new ProviderWebhookRequest(body, "not-base64-!!!", new Dictionary<string, string>())
        {
            WebhookSecret = Secret,
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("signature");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenJsonMalformed()
    {
        var adapter = BuildAdapter();
        var body = "{not valid json";
        var req = BuildRequest(body, Secret);

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenEventUnsupported()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = "evt_x",
            @event = "checkout.completed",
            data = new { id = "pix_abc", status = "PAID" }
        });
        var adapter = BuildAdapter();
        var req = BuildRequest(body, Secret);

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenProviderPaymentIdMissing()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = "evt_x",
            @event = "transparent.completed",
            data = new { status = "PAID" }
        });
        var adapter = BuildAdapter();
        var req = BuildRequest(body, Secret);

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("provider payment id");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenStatusMissing()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = "evt_x",
            @event = "transparent.completed",
            data = new { id = "pix_abc" }
        });
        var adapter = BuildAdapter();
        var req = BuildRequest(body, Secret);

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("status");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalid_WhenEnvelopeIdMissing()
    {
        var body = JsonSerializer.Serialize(new
        {
            @event = "transparent.completed",
            data = new { id = "pix_abc", status = "PAID" }
        });
        var adapter = BuildAdapter();
        var req = BuildRequest(body, Secret);

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("id");
    }

    [Theory]
    [InlineData("abacate_unit_test_webhook_secret_xyz_2026")]
    [InlineData("another-secret-for-leak-checks")]
    public async Task ParseWebhookAsync_ShouldNotLeakWebhookSecretInErrorMessages(string secretValue)
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();

        // Build a request whose signature is intentionally wrong so the
        // adapter emits a controlled error. We want to make sure that
        // error does not echo the secret back.
        var otherBody = Body("transparent.completed", "PAID");
        var req = new ProviderWebhookRequest(otherBody, "wrong-signature", new Dictionary<string, string>())
        {
            WebhookSecret = secretValue,
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(secretValue);

        // Also: a missing-secret scenario must not have echoed the value.
        var noSecret = new ProviderWebhookRequest(body, ComputeSignature(body, "some-other"), new Dictionary<string, string>())
        {
            WebhookSecret = null,
            ProviderAccountId = Guid.NewGuid()
        };
        var missing = await adapter.ParseWebhookAsync(noSecret, CancellationToken.None);
        missing.ErrorMessage.Should().NotContain("some-other");
    }

    [Theory]
    [InlineData("expected-signature-bytes-must-not-leak")]
    public async Task ParseWebhookAsync_ShouldNotLeakRawSignatureInErrorMessages(string signatureValue)
    {
        var body = Body("transparent.completed", "PAID");
        var adapter = BuildAdapter();
        var req = new ProviderWebhookRequest(body, signatureValue, new Dictionary<string, string>())
        {
            WebhookSecret = Secret,
            ProviderAccountId = Guid.NewGuid()
        };

        var result = await adapter.ParseWebhookAsync(req, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        // The signature could be a Base64 string of arbitrary length, so
        // we only assert that the raw header value the caller supplied is
        // not echoed back verbatim.
        result.ErrorMessage.Should().NotContain(signatureValue);
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldNotLeakRawBodyInErrorMessages()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = "evt_42",
            @event = "super-secret-internal-event-name",
            secret_token = "TOPSECRET-VALUE-XYZ",
            data = new { id = "pix_abc", status = "PAID" }
        });
        var adapter = BuildAdapter();
        // Sign the raw body so we get past HMAC; the normalizer rejection
        // is what we care about.
        var req = BuildRequest(body, Secret);
        // Mutate after signing to provoke a signature mismatch — but then
        // we can't assert about the payload message because signature will
        // fail first. Instead, let's sign correctly but use a body whose
        // event name is unsupported, which will trigger the normalizer's
        // unsupported event message.
        var unsupportedBody = JsonSerializer.Serialize(new
        {
            id = "evt_42",
            @event = "checkout.completed",
            secret_token = "TOPSECRET-VALUE-XYZ",
            data = new { id = "pix_abc", status = "PAID" }
        });
        var signedReq = BuildRequest(unsupportedBody, Secret);

        var result = await adapter.ParseWebhookAsync(signedReq, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("TOPSECRET-VALUE-XYZ");
        result.ErrorMessage.Should().NotContain("secret_token");
        result.ErrorMessage.Should().NotContain("super-secret-internal-event-name");
    }

    private sealed class NoopAbacatePayClient : IAbacatePayClient
    {
        public Task<AbacatePayCreateTransparentPixResponse> CreateTransparentPixAsync(
            AbacatePayCreateTransparentPixRequest request, string apiKey, CancellationToken cancellationToken)
            => throw new NotSupportedException("Covered by AbacatePayProviderAdapterTests.");

        public Task<AbacatePayCheckTransparentPixResponse> CheckTransparentPixAsync(
            string providerPaymentId, string apiKey, CancellationToken cancellationToken)
            => throw new NotSupportedException("Covered by AbacatePayClientTests.");

        public Task<AbacatePaySimulatePaymentResponse> SimulateTransparentPixPaymentAsync(
            string providerPaymentId, string apiKey, CancellationToken cancellationToken)
            => throw new NotSupportedException("Covered by AbacatePayClientTests.");
    }
}
