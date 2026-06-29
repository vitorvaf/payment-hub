using FluentAssertions;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

namespace PaymentHub.UnitTests.Infrastructure.Providers.AbacatePay.Webhooks;

public class AbacatePayWebhookNormalizerTests
{
    private readonly AbacatePayWebhookNormalizer _normalizer = new();

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenBodyEmpty()
    {
        var result = _normalizer.Normalize(string.Empty);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Empty");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenJsonMalformed()
    {
        var result = _normalizer.Normalize("{not valid json");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("JSON");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenEnvelopeIsNull()
    {
        var result = _normalizer.Normalize("null");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenEventMissing()
    {
        const string body = """{"id":"evt_1","data":{"id":"pix_abc","status":"PAID"}}""";

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("event");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenEventUnsupported()
    {
        const string body = """{"id":"evt_1","event":"checkout.completed","data":{"id":"pix_abc","status":"PAID"}}""";

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenEnvelopeIdMissing()
    {
        const string body = """{"event":"transparent.completed","data":{"id":"pix_abc","status":"PAID"}}""";

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("id");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenProviderPaymentIdMissing()
    {
        const string body = """{"id":"evt_1","event":"transparent.completed","data":{"status":"PAID"}}""";

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("provider payment id");
    }

    [Fact]
    public void Normalize_ShouldReturnInvalid_WhenStatusMissing()
    {
        const string body = """{"id":"evt_1","event":"transparent.completed","data":{"id":"pix_abc"}}""";

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("status");
    }

    [Theory]
    [InlineData("transparent.completed", "PAID", PaymentStatus.Approved)]
    [InlineData("transparent.completed", "APPROVED", PaymentStatus.Approved)]
    [InlineData("transparent.completed", "PENDING", PaymentStatus.Pending)]
    [InlineData("transparent.refunded", "REFUNDED", PaymentStatus.Refunded)]
    [InlineData("transparent.disputed", "DISPUTED", PaymentStatus.Pending)]
    [InlineData("transparent.lost", "FAILED", PaymentStatus.Failed)]
    public void Normalize_ShouldMapKnownEvents(string eventType, string rawStatus, PaymentStatus expected)
    {
        var body = BuildBody(eventType, rawStatus);

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeTrue();
        AbacatePayWebhookNormalizer.MapEvent(result.EventType, result.ProviderStatus!).Should().Be(expected);
    }

    private static string BuildBody(string eventType, string rawStatus) =>
        string.Format(
            """{{"id":"evt_1","event":"{0}","data":{{"id":"pix_abc","status":"{1}"}}}}""",
            eventType, rawStatus);

    [Fact]
    public void Normalize_ShouldPreserveRawPayloadForAuditAndDedup()
    {
        const string body = """{"id":"evt_1","event":"transparent.completed","data":{"id":"pix_abc","status":"PAID"}}""";

        var result = _normalizer.Normalize(body);

        result.RawPayloadJson.Should().Be(body);
    }

    [Fact]
    public void Normalize_ShouldExtractEventIdAndProviderPaymentId()
    {
        const string body = """{"id":"evt_42","event":"transparent.completed","data":{"id":"pix_xyz","status":"PAID"}}""";

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeTrue();
        result.EventId.Should().Be("evt_42");
        result.ProviderPaymentId.Should().Be("pix_xyz");
        result.ProviderStatus.Should().Be("PAID");
    }

    [Fact]
    public void Normalize_ShouldIgnoreAdditionalTopLevelFields()
    {
        // Future AbacatePay versions may add fields; we tolerate them
        // without breaking the parse.
        const string body = """
            {
              "id":"evt_1",
              "event":"transparent.completed",
              "apiVersion":2,
              "devMode":true,
              "unknown":"ignore-me",
              "data": {"id":"pix_abc","status":"PAID","metadata":{"tenantId":"t1"}}
            }
            """;

        var result = _normalizer.Normalize(body);

        result.IsValid.Should().BeTrue();
        result.ProviderPaymentId.Should().Be("pix_abc");
    }

    [Fact]
    public void MapEvent_ShouldNormalizeEventCaseAndWhitespace()
    {
        var status = AbacatePayWebhookNormalizer.MapEvent(" TRANSPARENT.REFUNDED ", "REFUNDED");

        status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void MapEvent_ShouldFallBackToPending_ForUnknownEvent()
    {
        var status = AbacatePayWebhookNormalizer.MapEvent("billing.forever", "PAID");

        status.Should().Be(PaymentStatus.Pending);
    }
}
