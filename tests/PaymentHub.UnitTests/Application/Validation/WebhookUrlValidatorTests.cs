using FluentAssertions;
using PaymentHub.Application.Tenants.Validation;

namespace PaymentHub.UnitTests.Application.Validation;

/// <summary>
/// Unit tests for <see cref="WebhookUrlValidator"/>.
///
/// Covers the SSRF-protection contract documented in
/// <c>docs/specs/011-security-and-compliance.md</c>:
///   - Well-formed absolute URI.
///   - HTTPS required, except http in Development for loopback hosts only.
///   - Loopback / RFC1918 / link-local / IMDS / unspecified / multicast / broadcast blocked.
///   - Hostnames 'localhost' / '*.localhost' / '*.local' blocked.
/// </summary>
public class WebhookUrlValidatorTests
{
    // ---------- Vacuous inputs ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_ShouldReturnTrue_ForNullOrWhitespace(string? input)
    {
        WebhookUrlValidator.IsAllowed(input, isDevelopment: false, out var reason)
            .Should().BeTrue("the validator must skip empty input and let 'MaximumLength' + 'When' rules decide");
        reason.Should().BeNull();
    }

    // ---------- Valid public HTTPS endpoints ----------

    [Theory]
    [InlineData("https://example.com/webhook")]
    [InlineData("https://hooks.example.com/payment-hub")]
    [InlineData("https://api.example.com:8443/webhook")]
    [InlineData("https://my-app.example.org/callbacks/payment")]
    [InlineData("https://8.8.8.8/webhook")] // public IP (not loopback, not RFC1918, not link-local)
    [InlineData("https://1.1.1.1/webhook")]
    public void IsAllowed_ShouldReturnTrue_ForPublicHttps(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeTrue($"public HTTPS endpoints must be allowed. url={url}");
        reason.Should().BeNull();
    }

    // ---------- Malformed / wrong scheme ----------

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("just-some-text")]
    [InlineData("no scheme at all here")]
    public void IsAllowed_ShouldReturnFalse_ForMalformedUri(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("well-formed");
    }

    [Theory]
    [InlineData("ftp://example.com/webhook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    [InlineData("ws://example.com/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForNonHttpScheme(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("HTTPS");
    }

    [Theory]
    [InlineData("http://example.com/webhook")]
    [InlineData("http://api.example.com/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForHttpOutsideDevelopment(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("HTTPS");
    }

    [Theory]
    [InlineData("http://example.com/webhook")]
    [InlineData("http://api.example.com/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForHttpOnPublicHost_EvenInDevelopment(string url)
    {
        // Development relaxes the HTTP exception to loopback ONLY.
        WebhookUrlValidator.IsAllowed(url, isDevelopment: true, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("loopback");
    }

    // ---------- localhost / loopback ----------

    [Theory]
    [InlineData("https://localhost/webhook")]
    [InlineData("https://localhost:5000/webhook")]
    [InlineData("https://LOCALHOST/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForLocalhostHostname(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("localhost");
    }

    [Theory]
    [InlineData("https://my.localhost/webhook")]
    [InlineData("https://api.localhost/webhook")]
    [InlineData("https://service.localhost/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForDotLocalhostHostname(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("localhost");
    }

    [Theory]
    [InlineData("https://printer.local/webhook")]
    [InlineData("https://nas.local/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForDotLocalHostname(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain(".local");
    }

    [Theory]
    [InlineData("https://127.0.0.1/webhook")]
    [InlineData("https://127.0.0.1:5000/webhook")]
    [InlineData("https://127.10.20.30/webhook")]
    [InlineData("https://127.255.255.254/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForLoopbackIpv4(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("loopback");
    }

    [Theory]
    [InlineData("https://[::1]/webhook")]
    [InlineData("https://[::1]:5000/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForLoopbackIpv6(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("loopback");
    }

    [Theory]
    [InlineData("https://[::ffff:127.0.0.1]/webhook")]
    [InlineData("https://[::ffff:127.0.0.1]:8443/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForIpv4MappedIpv6Loopback(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("loopback");
    }

    // ---------- RFC1918 ----------

    [Theory]
    [InlineData("https://10.0.0.1/webhook")]
    [InlineData("https://10.255.255.254/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForRfc1918_10(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("10.0.0.0/8");
    }

    [Theory]
    [InlineData("https://172.16.0.1/webhook")]
    [InlineData("https://172.20.10.5/webhook")]
    [InlineData("https://172.31.255.254/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForRfc1918_172_16_12(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("172.16.0.0/12");
    }

    [Theory]
    [InlineData("https://172.15.0.1/webhook")]
    [InlineData("https://172.32.0.1/webhook")]
    public void IsAllowed_ShouldReturnTrue_ForNonRfc1918_172Boundary(string url)
    {
        // 172.15.x.x and 172.32.x.x are outside the 172.16.0.0/12 block.
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeTrue($"the 172.16/12 boundary must not over-block. url={url}");
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("https://192.168.0.1/webhook")]
    [InlineData("https://192.168.1.10/webhook")]
    [InlineData("https://192.168.255.255/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForRfc1918_192_168(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("192.168.0.0/16");
    }

    // ---------- Link-local / IMDS / unspecified / broadcast ----------

    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data")] // AWS IMDS
    [InlineData("https://169.254.169.253/latest/meta-data")] // GCP equivalent
    [InlineData("https://169.254.1.1/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForLinkLocalImds(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("169.254.0.0/16");
    }

    [Theory]
    [InlineData("https://0.0.0.0/webhook")]
    [InlineData("https://0.0.0.0:5000/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForUnspecifiedIpv4(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("0.0.0.0");
    }

    [Theory]
    [InlineData("https://[::]/webhook")]
    [InlineData("https://[0:0:0:0:0:0:0:0]/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForUnspecifiedIpv6(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("::");
    }

    [Theory]
    [InlineData("https://[fe80::1]/webhook")]
    [InlineData("https://[fe80::abcd]:8443/webhook")]
    [InlineData("https://[febf:ffff::1]/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForIpv6LinkLocal(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("fe80::/10");
    }

    [Theory]
    [InlineData("https://255.255.255.255/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForBroadcastAddress(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("broadcast");
    }

    // ---------- Development exception: HTTP only allowed for loopback hosts ----------

    [Theory]
    [InlineData("http://localhost:5000/webhook")]
    [InlineData("http://localhost/webhook")]
    [InlineData("http://127.0.0.1:5000/webhook")]
    [InlineData("http://127.0.0.1/webhook")]
    public void IsAllowed_ShouldReturnTrue_ForHttpLoopback_InDevelopment(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: true, out var reason)
            .Should().BeTrue($"loopback HTTP must be allowed in Development. url={url}");
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:5000/webhook")]
    [InlineData("http://127.0.0.1:5000/webhook")]
    public void IsAllowed_ShouldReturnFalse_ForHttpLoopback_OutsideDevelopment(string url)
    {
        WebhookUrlValidator.IsAllowed(url, isDevelopment: false, out var reason)
            .Should().BeFalse($"loopback HTTP must NOT be allowed outside Development. url={url}");
        reason.Should().NotBeNullOrEmpty();
        reason.Should().Contain("HTTPS");
    }
}
