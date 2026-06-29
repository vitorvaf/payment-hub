using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.Infrastructure.Postgres.Security;

namespace PaymentHub.UnitTests.Infrastructure;

public class AesWebhookSecretProtectorTests
{
    [Fact]
    public void Protect_AndUnprotect_ShouldRoundTripPlainText()
    {
        var protector = CreateProtector(keyLength: 32);

        var protectedValue = protector.Protect("plain-webhook-secret");
        var recovered = protector.Unprotect(protectedValue);

        recovered.Should().Be("plain-webhook-secret");
    }

    [Fact]
    public void Protect_ShouldNotReturnPlainText()
    {
        var protector = CreateProtector(keyLength: 32);

        var protectedValue = protector.Protect("plain-webhook-secret");

        protectedValue.Should().NotBe("plain-webhook-secret");
        protectedValue.Should().NotContain("plain-webhook-secret");
    }

    [Fact]
    public void Protect_ShouldProduceDifferentOutput_ForSameInput_AcrossInvocations()
    {
        var protector = CreateProtector(keyLength: 32);

        var first = protector.Protect("plain-webhook-secret");
        var second = protector.Protect("plain-webhook-secret");

        first.Should().NotBe(second, "AES with random IV must yield different ciphertexts for the same plaintext");
    }

    [Fact]
    public void Unprotect_ShouldThrow_WhenPlaintextLacksExpectedPurpose()
    {
        var protector = CreateProtector(keyLength: 32);

        // AES-CBC may either fail on padding (CryptographicException) or decrypt to garbage
        // that lacks the expected purpose marker (InvalidOperationException).
        var bogusCiphertext = Convert.ToBase64String(new byte[32]);

        var act = () => protector.Unprotect(bogusCiphertext);

        act.Should().Throw<Exception>()
            .Which.Should().Match(e => e is InvalidOperationException || e is CryptographicException);
    }

    [Fact]
    public void Unprotect_ShouldThrow_WhenInputIsEmpty()
    {
        var protector = CreateProtector(keyLength: 32);

        var act = () => protector.Unprotect(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unprotect_ShouldThrow_WhenInputIsNotValidBase64()
    {
        var protector = CreateProtector(keyLength: 32);

        var act = () => protector.Unprotect("!!!not-base64!!!");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Protect_ShouldThrow_WhenPlainTextIsEmpty()
    {
        var protector = CreateProtector(keyLength: 32);

        var act = () => protector.Protect(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Protect_ShouldThrow_WhenPlainTextIsNull()
    {
        var protector = CreateProtector(keyLength: 32);

        var act = () => protector.Protect(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenWebhookSecretEncryptionKeyIsEmpty()
    {
        var options = Options.Create(new PaymentHubOptions { WebhookSecretEncryptionKey = string.Empty });

        var act = () => new AesWebhookSecretProtector(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WebhookSecretEncryptionKey*");
    }

    [Fact]
    public void Unprotect_WithDifferentKey_ShouldThrow()
    {
        var first = CreateProtector(keyLength: 32, keySuffix: "first");
        var second = CreateProtector(keyLength: 32, keySuffix: "second");

        var protectedValue = first.Protect("plain-webhook-secret");

        var act = () => second.Unprotect(protectedValue);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Protect_ShouldPadShortKeys_ToReach32Bytes()
    {
        var protector = CreateProtector(keyLength: 16);

        var protectedValue = protector.Protect("plain-webhook-secret");

        var recovered = protector.Unprotect(protectedValue);
        recovered.Should().Be("plain-webhook-secret");
    }

    private static AesWebhookSecretProtector CreateProtector(int keyLength = 32, string keySuffix = "default")
    {
        var raw = $"webhook-key-{keySuffix}-".PadRight(keyLength, 'x');
        var options = Options.Create(new PaymentHubOptions { WebhookSecretEncryptionKey = raw });
        return new AesWebhookSecretProtector(options);
    }
}