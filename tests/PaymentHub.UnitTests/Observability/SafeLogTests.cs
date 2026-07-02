using FluentAssertions;
using PaymentHub.Application.Observability;
using PaymentHub.Domain.Enums;

namespace PaymentHub.UnitTests.Observability;

/// <summary>
/// Tests for <see cref="SafeLog"/>. The helpers exist to keep sensitive
/// values out of structured logs — coverage here is intentionally narrow
/// and focused on the boundaries (null, empty, long).
/// </summary>
public class SafeLogTests
{
    [Fact]
    public void Id_ShouldReturnDash_WhenValueIsNull()
    {
        SafeLog.Id((Guid?)null).Should().Be("-");
    }

    [Fact]
    public void Id_ShouldReturnDash_WhenValueIsEmpty()
    {
        SafeLog.Id(Guid.Empty).Should().Be("-");
    }

    [Fact]
    public void Id_ShouldReturnFirstEightChars_WhenValueIsGuid()
    {
        // Use a hex-only GUID so Guid.Parse succeeds (the regex accepts
        // mixed chars but Guid.Parse rejects non-hex digits in segments).
        var id = Guid.Parse("abcd1234-5678-90ab-cdef-1234567890ab");

        SafeLog.Id(id).Should().Be("abcd1234");
    }

    [Fact]
    public void Length_ShouldReturnZero_WhenValueIsNull()
    {
        SafeLog.Length(null).Should().Be(0);
    }

    [Fact]
    public void Length_ShouldReturnZero_WhenValueIsEmpty()
    {
        SafeLog.Length(string.Empty).Should().Be(0);
    }

    [Fact]
    public void Length_ShouldReturnCharCount_WhenValueIsPresent()
    {
        SafeLog.Length("hello").Should().Be(5);
    }

    [Fact]
    public void Flag_ShouldReturnLabeledYes_WhenBooleanIsTrue()
    {
        SafeLog.Flag("has_secret", true).Should().Be("has_secret=yes");
    }

    [Fact]
    public void Flag_ShouldReturnLabeledNo_WhenBooleanIsFalse()
    {
        SafeLog.Flag("has_secret", false).Should().Be("has_secret=no");
    }

    [Fact]
    public void Flag_ShouldReturnDash_WhenBooleanIsNull()
    {
        SafeLog.Flag("has_secret", null).Should().Be("has_secret=-");
    }

    [Fact]
    public void Category_ShouldReturnEnumName()
    {
        // Use the existing WebhookDispatcherCategory from the domain to
        // exercise the generic constraint.
        SafeLog.Category(WebhookDispatcherCategory.HttpFailure)
            .Should().Be("HttpFailure");
    }

    [Fact]
    public void Category_ShouldHandleMultiWordEnumValues()
    {
        SafeLog.Category(WebhookDispatcherCategory.UnexpectedDispatcherError)
            .Should().Be("UnexpectedDispatcherError");
    }
}
