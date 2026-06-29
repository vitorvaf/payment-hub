using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;

namespace PaymentHub.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="RegisterApplicationClientValidator"/>.
///
/// Exercises the SSRF-protection contract documented in
/// <c>docs/specs/011-security-and-compliance.md</c> at the validator boundary
/// (the same boundary the API controller validates before invoking the handler).
/// </summary>
public class RegisterApplicationClientValidatorTests
{
    private readonly Mock<IRuntimeEnvironment> _environment = new(MockBehavior.Strict);
    private readonly RegisterApplicationClientValidator _validator;

    public RegisterApplicationClientValidatorTests()
    {
        // Default: production-like environment (most tests reject loopback/HTTP).
        _environment.Setup(e => e.IsDevelopment).Returns(false);
        _validator = new RegisterApplicationClientValidator(_environment.Object);
    }

    private static RegisterApplicationClientRequestDto BuildRequest(string? webhookUrl)
        => new(
            TenantId: Guid.NewGuid(),
            Name: "App",
            WebhookUrl: webhookUrl,
            WebhookSecret: null,
            DefaultProvider: null);

    // ---------- Boundary cases ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldNotValidateWebhookUrl_WhenNullOrWhitespace(string? input)
    {
        // The validator should not flag empty values — absence is a valid choice.
        var result = _validator.TestValidate(BuildRequest(input));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ShouldPass_WhenTenantIdAndNameValid()
    {
        var result = _validator.TestValidate(BuildRequest(null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ---------- Valid public HTTPS endpoints ----------

    [Theory]
    [InlineData("https://example.com/webhook")]
    [InlineData("https://hooks.example.com/payment-hub")]
    [InlineData("https://api.example.com:8443/webhook")]
    [InlineData("https://my-app.example.org/callbacks/payment")]
    public void Validate_ShouldAcceptPublicHttps(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldNotHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Reject HTTP outside Development ----------

    [Theory]
    [InlineData("http://example.com/webhook")]
    [InlineData("http://api.example.com/webhook")]
    public void Validate_ShouldRejectHttp_OnPublicHost_OutsideDevelopment(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl)
            .WithErrorMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.");
    }

    [Theory]
    [InlineData("http://example.com/webhook")]
    [InlineData("http://api.example.com/webhook")]
    public void Validate_ShouldRejectHttp_OnPublicHost_EvenInDevelopment(string url)
    {
        _environment.Setup(e => e.IsDevelopment).Returns(true);
        var devValidator = new RegisterApplicationClientValidator(_environment.Object);

        var result = devValidator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl)
            .WithErrorMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.");
    }

    // ---------- Reject localhost ----------

    [Theory]
    [InlineData("https://localhost/webhook")]
    [InlineData("https://localhost:5000/webhook")]
    [InlineData("https://api.localhost/webhook")]
    [InlineData("https://my.localhost/webhook")]
    public void Validate_ShouldRejectLocalhost(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Reject loopback IPs ----------

    [Theory]
    [InlineData("https://127.0.0.1/webhook")]
    [InlineData("https://127.10.20.30/webhook")]
    [InlineData("https://[::1]/webhook")]
    [InlineData("https://[::ffff:127.0.0.1]/webhook")]
    public void Validate_ShouldRejectLoopback(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Reject RFC1918 ----------

    [Theory]
    [InlineData("https://10.0.0.1/webhook")]
    [InlineData("https://172.16.0.1/webhook")]
    [InlineData("https://172.31.255.255/webhook")]
    [InlineData("https://192.168.1.10/webhook")]
    public void Validate_ShouldRejectRfc1918(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Reject link-local / IMDS ----------

    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://169.254.1.1/webhook")]
    [InlineData("https://0.0.0.0/webhook")]
    public void Validate_ShouldRejectLinkLocalAndUnspecified(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Reject malformed / wrong-scheme ----------

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/webhook")]
    [InlineData("ftp://example.com/webhook")]
    [InlineData("file:///etc/passwd")]
    public void Validate_ShouldRejectMalformedOrWrongScheme(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Development exception for HTTP loopback ----------

    [Theory]
    [InlineData("http://localhost:5000/webhook")]
    [InlineData("http://127.0.0.1:5000/webhook")]
    [InlineData("http://localhost/webhook")]
    [InlineData("http://127.0.0.1/webhook")]
    public void Validate_ShouldAcceptHttpLoopback_InDevelopment(string url)
    {
        _environment.Setup(e => e.IsDevelopment).Returns(true);
        var devValidator = new RegisterApplicationClientValidator(_environment.Object);

        var result = devValidator.TestValidate(BuildRequest(url));
        result.ShouldNotHaveValidationErrorFor(x => x.WebhookUrl);
    }

    [Theory]
    [InlineData("http://localhost:5000/webhook")]
    [InlineData("http://127.0.0.1:5000/webhook")]
    public void Validate_ShouldRejectHttpLoopback_OutsideDevelopment(string url)
    {
        var result = _validator.TestValidate(BuildRequest(url));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- MaximumLength retained ----------

    [Fact]
    public void Validate_ShouldRejectWebhookUrl_LongerThan2000Chars()
    {
        var longUrl = "https://example.com/" + new string('a', 2050);
        var result = _validator.TestValidate(BuildRequest(longUrl));
        result.ShouldHaveValidationErrorFor(x => x.WebhookUrl);
    }

    // ---------- Other unrelated rules still pass ----------

    [Fact]
    public void Validate_ShouldStillEnforceTenantIdNotEmpty()
    {
        var request = new RegisterApplicationClientRequestDto(
            TenantId: Guid.Empty,
            Name: "App",
            WebhookUrl: null,
            WebhookSecret: null,
            DefaultProvider: null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.TenantId);
    }

    [Fact]
    public void Validate_ShouldStillEnforceNameNotEmpty()
    {
        var request = new RegisterApplicationClientRequestDto(
            TenantId: Guid.NewGuid(),
            Name: "",
            WebhookUrl: null,
            WebhookSecret: null,
            DefaultProvider: null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }
}
