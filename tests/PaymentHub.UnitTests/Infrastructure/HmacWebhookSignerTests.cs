using FluentAssertions;
using PaymentHub.Infrastructure.Postgres.Security;

namespace PaymentHub.UnitTests.Infrastructure;

public class HmacWebhookSignerTests
{
    [Fact]
    public void Sign_ShouldUseTimestampAndPayload()
    {
        var signer = new HmacWebhookSigner();
        var payload = "{\"eventId\":\"evt-1\"}";
        var timestamp = "1781625600";
        var secret = "shared-secret";

        var signature = signer.Sign(payload, secret, timestamp);

        signature.Should().Be("59f0266689c5688a562465923ef5202d36615de92ae718867e5cb1089e2c0299");
        signer.Verify(payload, secret, timestamp, signature).Should().BeTrue();
        signer.Verify(payload, secret, "1781625601", signature).Should().BeFalse();
    }
}
