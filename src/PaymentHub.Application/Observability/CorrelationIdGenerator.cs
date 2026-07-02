using System.Text.RegularExpressions;

namespace PaymentHub.Application.Observability;

/// <summary>
/// Pure helpers for the request-scoped <c>CorrelationId</c> that flows through
/// checkout -&gt; provider -&gt; webhook -&gt; Inbox -&gt; Outbox -&gt; application
/// webhook. Slice 9-O1 introduces the helper + the API middleware
/// (<c>CorrelationIdMiddleware</c>) + the propagation through
/// <c>ICorrelationIdAccessor</c>. See <c>docs/specs/012-observability-and-audit.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>CorrelationId</c> has a deliberately permissive but bounded character
/// set so it is safe to use as a header value, in log scope, and as a URL-safe
/// identifier without escaping. The regex accepts ASCII letters, digits and
/// dashes; the length window (8..128) catches the GUID-N style produced by
/// <see cref="New"/> while still accepting shorter human-friendly values for
/// the future.
///
/// </para>
/// <para>
/// We do NOT log the raw <c>CorrelationId</c> in the rejection path
/// (<see cref="IsValid"/>) to avoid log injection — middleware only logs the
/// header name.
/// </para>
/// </remarks>
public static class CorrelationIdGenerator
{
    /// <summary>
    /// Header name accepted on inbound HTTP requests and emitted on outbound
    /// webhook deliveries (Slice 9-O1 decision #2).
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// HTTP <c>Items</c> key used to thread the resolved <c>CorrelationId</c>
    /// through the request pipeline (consumed by <c>HttpTenantContext</c> in the
    /// same dictionary and by <c>HttpCorrelationIdAccessor</c>).
    /// </summary>
    public const string HttpContextItemsKey = "correlationId";

    /// <summary>
    /// Length window the helper accepts as a valid <c>CorrelationId</c>.
    /// The lower bound catches a useful prefix of the canonical GUID-N form
    /// without admitting empty or accidental values; the upper bound protects
    /// the database column (<c>correlation_id VARCHAR(64)</c>).
    /// </summary>
    public const int MinLength = 8;
    public const int MaxLength = 128;

    // Compiled once. The regex is intentionally small and side-effect-free.
    private static readonly Regex Pattern = new(
        "^[A-Za-z0-9\\-]{" + MinLength + "," + MaxLength + "}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Generates a fresh <c>CorrelationId</c> as a GUID in the compact
    /// "N" format (32 hex chars, no dashes). Compatible with the validation
    /// regex and the database column.
    /// </summary>
    public static string New() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> is a syntactically
    /// valid <c>CorrelationId</c> (charset + length window). Never throws,
    /// never logs the candidate value (slice 9-O1.4 anti-leak rule).
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return Pattern.IsMatch(value);
    }
}
