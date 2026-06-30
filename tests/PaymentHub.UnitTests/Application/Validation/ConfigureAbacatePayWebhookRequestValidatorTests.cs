using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;

namespace PaymentHub.UnitTests.Application.Validation;

/// <summary>
/// Validator tests for the Slice 2-C
/// <c>ConfigureAbacatePayWebhookRequestValidator</c>.
///
/// Coverage:
/// - Happy path (no fields) is rejected as "events is null"; the
///   validator covers CallbackUrl, Events and WebhookSecret per
///   documented rules.
/// - Each field validation rule is exercised.
/// </summary>
public class ConfigureAbacatePayWebhookRequestValidatorTests
{
    private readonly Mock<IRuntimeEnvironment> _env = new();

    public ConfigureAbacatePayWebhookRequestValidatorTests()
    {
        _env.SetupGet(e => e.IsDevelopment).Returns(false);
    }

    [Fact]
    public void Validator_ShouldExist_AndAcceptEmptyRequest()
    {
        // No "all-null" rejection — empty body is a legitimate "I want
        // to register remote status only" call. The handler decides
        // whether to do anything with that.
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            null, null, null, false));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://merchant.example.com/webhook")]
    [InlineData("https://api.abacatepay.com/v2/webhooks")]
    public void Validator_ShouldAcceptPublicHttpsCallbackUrl(string url)
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            url, null, null, false));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://example.com/webhook")] // HTTP outside Dev
    [InlineData("https://localhost/webhook")] // loopback HTTPS
    [InlineData("https://10.0.0.1/webhook")] // RFC1918
    [InlineData("https://192.168.0.10/webhook")] // RFC1918
    [InlineData("https://172.16.0.5/webhook")] // RFC1918
    [InlineData("https://169.254.169.254/webhook")] // IMDS
    [InlineData("ftp://example.com/webhook")]
    [InlineData("not-a-url")]
    public void Validator_ShouldRejectInsecureOrPrivateCallbackUrl(string url)
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            url, null, null, false));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldAllowHttpLoopback_WhenDevelopment()
    {
        _env.SetupGet(e => e.IsDevelopment).Returns(true);
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            "http://localhost:8080/webhook", null, null, false));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_ShouldRejectHttpNonLoopback_WhenDevelopment()
    {
        _env.SetupGet(e => e.IsDevelopment).Returns(true);
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            "http://example.com/webhook", null, null, false));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldAcceptAllowedAbacatePayEvents()
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            null,
            new[] { "transparent.completed", "transparent.refunded", "transparent.disputed", "transparent.lost" },
            null, false));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("completed")] // missing prefix
    [InlineData("transparent.created")] // not in whitelist
    [InlineData("checkout.completed")] // unrelated event
    public void Validator_ShouldRejectDisallowedEvents(string evt)
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            null, new[] { evt }, null, false));
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validator_ShouldRejectEmptyOrWhitespaceEvent(string? evt)
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            null, new[] { evt! }, null, false));
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "a-very-long-secret-that-exceeds-the-five-hundred-character-limit-" +
                "suffix-to-push-it-over-the-five-hundred-character-maximum-limit-end")]
    public void Validator_ShouldRejectOutOfRangeWebhookSecret(string secret)
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            null, null, secret, false));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldAcceptInRangeWebhookSecret()
    {
        var validator = CreateValidator();
        var result = validator.TestValidate(new ConfigureAbacatePayWebhookRequestDto(
            null, null, "abcdefghijklmnop", false));
        result.IsValid.Should().BeTrue();
    }

    private ConfigureAbacatePayWebhookRequestValidator CreateValidator()
        => new(_env.Object);
}
