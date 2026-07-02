using PaymentHub.Domain.Enums;

namespace PaymentHub.Application.Observability;

/// <summary>
/// Safe property helpers for the Payment Hub's structured logs. Slice 9-O1
/// introduces the helpers so call sites can log rich context WITHOUT leaking
/// sensitive values (apiKey, webhookSecret, raw payload, signature, body).
///
/// <para>
/// Use <c>SafeLog.Id(Guid)</c> for entity identifiers,
/// <c>SafeLog.Length(string?)</c> for opaque strings whose size is the only
/// non-sensitive attribute, <c>SafeLog.Bool(string, bool?)</c> for
/// pre-computed flags, <c>SafeLog.Category&lt;T&gt;(T)</c> for enums (the
/// enum name only — never the <c>.ToString()</c> payload).
/// </para>
///
/// <para><b>Anti-leak audit</b>: <c>scripts/agent-docs-check.sh</c> greps for
/// <c>Log(Warning|Information|Error|Debug)</c> invocations that interpolate
/// <c>apiKey</c>, <c>webhookSecret</c>, <c>brCodeBase64</c>, <c>rawPayload</c>,
/// <c>signature</c>, <c>Authorization</c> or <c>body</c>. The audit fails
/// the build when a hit is found. <c>NoLeakLogTests</c> covers the same
/// property values at runtime.
/// </para>
/// </summary>
public static class SafeLog
{
    /// <summary>
    /// Logs an entity identifier as a short string ("abcd1234"). The original
    /// guid is never reconstructed, so log-aggregation pipelines can group by
    /// the prefix without storing the full id. This is intentionally lossy:
    /// the slice 9-O1.4 anti-leak gate guarantees identifiers never appear
    /// verbatim in logs.
    /// </summary>
    public static string Id(Guid? id)
    {
        if (id is null || id.Value == Guid.Empty) return "-";
        var n = id.Value.ToString("N");
        return n[..Math.Min(8, n.Length)];
    }

    /// <summary>
    /// Returns the length of an opaque string without surfacing its content.
    /// Use for raw bodies, signatures, webhook secrets or apiKey values
    /// where the byte count is useful for triage but the value is not.
    /// </summary>
    public static int Length(string? value)
        => string.IsNullOrEmpty(value) ? 0 : value.Length;

    /// <summary>
    /// Returns a nullable boolean as a canonical flag ("yes"/"no"/"-"). The
    /// string format is fixed so dashboards can group on a stable value
    /// without needing to interpret three different log message shapes.
    /// </summary>
    public static string Flag(string label, bool? value) => value switch
    {
        true => $"{label}=yes",
        false => $"{label}=no",
        _ => $"{label}=-",
    };

    /// <summary>
    /// Returns the canonical name of an enum value as a string. Used for
    /// <see cref="WebhookDispatcherCategory"/> and <see cref="PaymentStatus"/>
    /// in <c>LastError</c> columns and structured log lines — never include
    /// the original payload.
    /// </summary>
    public static string Category<TEnum>(TEnum value) where TEnum : struct, Enum
        => Enum.GetName(typeof(TEnum), value) ?? value.ToString();
}
