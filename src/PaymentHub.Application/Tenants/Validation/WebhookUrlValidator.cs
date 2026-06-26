using System.Net;

namespace PaymentHub.Application.Tenants.Validation;

/// <summary>
/// Pure, side-effect-free validator for <c>ApplicationClient.WebhookUrl</c>.
///
/// Centralizes the SSRF-protection rules required by
/// <c>docs/specs/011-security-and-compliance.md</c>:
///   - Absolute URI required.
///   - HTTPS required; <c>http</c> allowed in <c>Development</c> ONLY for loopback hosts.
///   - For HTTPS destinations: loopback / RFC1918 / link-local / IMDS / unspecified /
///     multicast / broadcast hosts are blocked, plus <c>localhost</c>, <c>*.localhost</c>
///     and <c>*.local</c> hostnames.
///   - For HTTP destinations outside <c>Development</c>: rejected (must be HTTPS).
///   - For HTTP destinations in <c>Development</c>: only loopback hosts are accepted.
///
/// Helper has no DI dependency, no logging and no exceptions so it can be
/// reused by FluentValidation rules and unit tests without setup.
/// </summary>
internal static class WebhookUrlValidator
{
    /// <summary>
    /// Returns <c>true</c> when the supplied <paramref name="value"/> is acceptable as
    /// a webhook destination. When <c>false</c>, <paramref name="reason"/> carries a
    /// short human-readable explanation safe to surface in API validation messages.
    /// </summary>
    public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = null;
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            reason = "WebhookUrl must be a well-formed absolute URI.";
            return false;
        }

        var scheme = uri.Scheme;
        var isHttps = string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        if (!isHttps && !isHttp)
        {
            reason = "WebhookUrl scheme must be HTTPS.";
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
        {
            reason = "WebhookUrl must include a host.";
            return false;
        }

        if (isHttps)
        {
            // HTTPS destinations must reach a public endpoint. Block loopback,
            // blocked hostnames, blocked IPs, and IMDS regardless of host string.
            if (IsLoopbackHost(host, out var loopbackReason))
            {
                reason = loopbackReason;
                return false;
            }

            if (IsBlockedHostName(host, out var hostReason))
            {
                reason = hostReason;
                return false;
            }

            if (IPAddress.TryParse(host, out var address) &&
                IsBlockedAddress(address, out var ipReason))
            {
                reason = ipReason;
                return false;
            }

            reason = null;
            return true;
        }

        // isHttp path.
        if (!isDevelopment)
        {
            reason = "WebhookUrl scheme must be HTTPS outside Development.";
            return false;
        }

        if (!IsLoopbackHost(host, out _))
        {
            reason = "HTTP WebhookUrl is only allowed for loopback hosts in Development.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// True when the host is a loopback name (<c>localhost</c>, <c>*.localhost</c>)
    /// or a loopback IP (after IPv6-mapped IPv4 normalization).
    /// </summary>
    private static bool IsLoopbackHost(string host, out string? reason)
    {
        var normalized = host.TrimEnd('.').ToLowerInvariant();

        if (string.Equals(normalized, "localhost", StringComparison.Ordinal))
        {
            reason = "WebhookUrl host 'localhost' is loopback.";
            return true;
        }

        if (normalized.EndsWith(".localhost", StringComparison.Ordinal))
        {
            reason = "WebhookUrl host '*.localhost' is loopback.";
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (IPAddress.IsLoopback(address))
            {
                reason = "WebhookUrl host is in the loopback range (127.0.0.0/8 or ::1).";
                return true;
            }
        }

        reason = null;
        return false;
    }

    private static bool IsBlockedHostName(string host, out string reason)
    {
        var normalized = host.TrimEnd('.').ToLowerInvariant();

        if (string.Equals(normalized, "localhost", StringComparison.Ordinal))
        {
            reason = "WebhookUrl host 'localhost' is blocked.";
            return true;
        }

        if (normalized.EndsWith(".localhost", StringComparison.Ordinal))
        {
            reason = "WebhookUrl host '*.localhost' is blocked.";
            return true;
        }

        if (normalized.EndsWith(".local", StringComparison.Ordinal))
        {
            reason = "WebhookUrl host '*.local' is blocked (mDNS/link-local).";
            return true;
        }

        reason = null!;
        return false;
    }

    private static bool IsBlockedAddress(IPAddress address, out string reason)
    {
        // IPv6 mapped IPv4 (e.g. ::ffff:127.0.0.1) must be normalized to the
        // underlying IPv4 address before applying loopback/RFC1918 rules.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
        {
            reason = "WebhookUrl host is in the loopback range (127.0.0.0/8 or ::1).";
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // RFC1918: 10.0.0.0/8
                if (bytes[0] == 10)
                {
                    reason = "WebhookUrl host is in the RFC1918 range 10.0.0.0/8.";
                    return true;
                }

                // RFC1918: 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    reason = "WebhookUrl host is in the RFC1918 range 172.16.0.0/12.";
                    return true;
                }

                // RFC1918: 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    reason = "WebhookUrl host is in the RFC1918 range 192.168.0.0/16.";
                    return true;
                }

                // Link-local / IMDS: 169.254.0.0/16
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    reason = "WebhookUrl host is in the link-local/IMDS range 169.254.0.0/16.";
                    return true;
                }

                // Unspecified: 0.0.0.0
                if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                {
                    reason = "WebhookUrl host is the unspecified address 0.0.0.0.";
                    return true;
                }

                // Broadcast: 255.255.255.255
                if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255)
                {
                    reason = "WebhookUrl host is the broadcast address 255.255.255.255.";
                    return true;
                }
            }
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 16)
            {
                // Unspecified: ::
                var isUnspecified = true;
                for (var i = 0; i < 16; i++)
                {
                    if (bytes[i] != 0)
                    {
                        isUnspecified = false;
                        break;
                    }
                }

                if (isUnspecified)
                {
                    reason = "WebhookUrl host is the unspecified IPv6 address ::.";
                    return true;
                }

                // Link-local: fe80::/10
                if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                {
                    reason = "WebhookUrl host is in the IPv6 link-local range fe80::/10.";
                    return true;
                }
            }
        }

        reason = null!;
        return false;
    }
}
