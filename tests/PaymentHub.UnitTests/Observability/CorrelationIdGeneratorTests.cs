using FluentAssertions;
using PaymentHub.Application.Observability;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Observability;

/// <summary>
/// Pure tests for <see cref="CorrelationIdGenerator"/>. The slice 9-O1
/// regex is the canonical entry point for inbound HTTP header validation
/// and outbound header emission, so the regex + helper coverage is
/// intentionally narrow and exhaustive.
/// </summary>
public class CorrelationIdGeneratorTests
{
    [Fact]
    public void New_ShouldReturnGuidN_OfExpectedLength()
    {
        var id = CorrelationIdGenerator.New();

        id.Should().HaveLength(32);
        id.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Theory]
    [InlineData(CorrelationIdTestHelper.ValidId)]
    [InlineData(CorrelationIdTestHelper.ValidIdAlternate)]
    [InlineData("12345678")]               // exact lower bound (8 chars)
    [InlineData("a-b-c-d-e-f-g-h")]        // mixed charset
    public void IsValid_ShouldAccept_ValuesInsideCharsetAndLengthWindow(string candidate)
    {
        CorrelationIdGenerator.IsValid(candidate).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]                                       // null
    [InlineData("")]                                         // empty
    [InlineData(" ")]                                        // whitespace
    [InlineData("short")]                                    // below 8 chars
    [InlineData("contains spaces in middle")]                // forbidden char
    [InlineData("contains_underscore")]                      // forbidden char
    [InlineData("contains/slash")]                           // forbidden char
    [InlineData("contains+plus")]                            // forbidden char
    [InlineData("contains=equal")]                           // forbidden char
    [InlineData("contains\"quote")]                          // forbidden char
    public void IsValid_ShouldReject_ValuesOutsideCharsetOrLengthWindow(string? candidate)
    {
        CorrelationIdGenerator.IsValid(candidate).Should().BeFalse();
    }

    [Fact]
    public void HeaderName_ShouldMatchXCorrelationIdContract()
    {
        // The header name is the contract surface for both inbound
        // middleware and outbound dispatcher. Pin the value to detect
        // accidental rename drift.
        CorrelationIdGenerator.HeaderName.Should().Be("X-Correlation-Id");
    }

    [Fact]
    public void HttpContextItemsKey_ShouldMatchConventionalCamelCaseSlot()
    {
        // HttpTenantContext reads sibling keys from HttpContext.Items; the
        // CorrelationId slot lives in the same dictionary. Pin the key so
        // accidental rename is caught at compile time.
        CorrelationIdGenerator.HttpContextItemsKey.Should().Be("correlationId");
    }

    [Fact]
    public void New_ShouldReturnDifferentValuesAcrossInvocations()
    {
        var a = CorrelationIdGenerator.New();
        var b = CorrelationIdGenerator.New();

        a.Should().NotBe(b);
    }

    [Fact]
    public void New_ShouldAlwaysBeAcceptedByIsValid()
    {
        // Property: any value produced by New() passes IsValid. Locked
        // here so future changes to either method cannot regress the
        // invariant.
        for (var i = 0; i < 25; i++)
        {
            CorrelationIdGenerator.IsValid(CorrelationIdGenerator.New()).Should().BeTrue();
        }
    }

    [Fact]
    public void MaxLength_ShouldBoundByColumnSpec()
    {
        // The Domain layer truncates to 64 chars before persist; the regex
        // allows up to 128. Both windows are intentionally larger than the
        // DB column so the storage cap is the strict gate.
        CorrelationIdGenerator.MinLength.Should().Be(8);
        CorrelationIdGenerator.MaxLength.Should().Be(128);
    }
}
