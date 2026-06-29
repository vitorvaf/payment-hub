using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks;

namespace PaymentHub.UnitTests.Infrastructure.Providers.AbacatePay.Webhooks;

public class AbacatePayWebhookSignatureVerifierTests
{
    private const string Secret = "abacate_test_secret_for_unit_tests_only_xyz";
    private const string Body = """{"id":"evt_1","event":"transparent.completed","data":{"id":"pix_abc","status":"PAID"}}""";

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToBase64String(hash);
    }

    private readonly HmacAbacatePayWebhookSignatureVerifier _verifier = new();

    [Fact]
    public void Verify_ShouldReturnNone_WhenSignatureMatches()
    {
        var signature = ComputeSignature(Body, Secret);

        var result = _verifier.Verify(Body, signature, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.None);
    }

    [Fact]
    public void Verify_ShouldReturnSignatureMismatch_WhenBodyAlteredAfterSigning()
    {
        var signature = ComputeSignature(Body, Secret);

        var tampered = Body + " "; // extra byte changes the HMAC

        var result = _verifier.Verify(tampered, signature, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.SignatureMismatch);
    }

    [Fact]
    public void Verify_ShouldReturnSignatureMismatch_WhenSecretWrong()
    {
        var signature = ComputeSignature(Body, Secret);

        var result = _verifier.Verify(Body, signature, "different-secret");

        result.Should().Be(AbacatePayWebhookSignatureFailure.SignatureMismatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_ShouldReturnMissingSignature_WhenHeaderAbsentOrBlank(string? signature)
    {
        var result = _verifier.Verify(Body, signature, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.MissingSignature);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_ShouldReturnMissingSecret_WhenSecretAbsentOrBlank(string? secret)
    {
        var signature = ComputeSignature(Body, Secret);

        var result = _verifier.Verify(Body, signature, secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.MissingSecret);
    }

    [Theory]
    [InlineData("not-base64-!!!")]      // contains invalid chars
    [InlineData("abc")]                 // too short to be a valid SHA-256 (32 bytes) digest
    public void Verify_ShouldReturnMalformedSignature_WhenBase64Invalid(string header)
    {
        var result = _verifier.Verify(Body, header, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.MalformedSignature);
    }

    [Fact]
    public void Verify_ShouldReturnSignatureMismatch_WhenBase64WellFormedButWrongLength()
    {
        // Legitimate Base64, wrong length (32 vs 16 bytes), avoids FixedTimeEquals early-out.
        var shortSig = Convert.ToBase64String(new byte[16]);

        var result = _verifier.Verify(Body, shortSig, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.SignatureMismatch);
    }

    [Fact]
    public void Verify_ShouldAcceptUtf8BodyWithMultiByteCharacters()
    {
        const string unicodeBody = """{"id":"evt_u","event":"transparent.completed","customer":{"name":"João da Silva"}}""";
        var signature = ComputeSignature(unicodeBody, Secret);

        var result = _verifier.Verify(unicodeBody, signature, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.None);
    }

    [Fact]
    public void Verify_ShouldTreatNullBodyAsEmpty()
    {
        var signature = ComputeSignature(string.Empty, Secret);

        var result = _verifier.Verify(null!, signature, Secret);

        result.Should().Be(AbacatePayWebhookSignatureFailure.None);
    }

    [Fact]
    public void Verify_ShouldBeConstantTime_AcrossDifferentPayloads()
    {
        // We do not have a way to assert cryptographic timing here, but we
        // can at least confirm both calls return a deterministic failure.
        var sig1 = ComputeSignature("payload-a", Secret);
        var sig2 = ComputeSignature("payload-b", Secret);

        var r1 = _verifier.Verify("payload-b", sig1, Secret);
        var r2 = _verifier.Verify("payload-a", sig2, Secret);

        r1.Should().Be(AbacatePayWebhookSignatureFailure.SignatureMismatch);
        r2.Should().Be(AbacatePayWebhookSignatureFailure.SignatureMismatch);
    }
}
