using MyAdventure.Core.Services;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class NumberFormatterTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(999.99, "999.99")]
    [InlineData(1000, "1.00 K")]
    [InlineData(1500, "1.50 K")]
    [InlineData(1_000_000, "1.00 M")]
    [InlineData(1_500_000_000, "1.50 B")]
    [InlineData(1e12, "1.00 T")]
    [InlineData(2.5e15, "2.50 Qa")]
    public void Format_ShouldReturnExpectedSuffix(double input, string expected) =>
        NumberFormatter.Format(input).ShouldBe(expected);

    [Fact]
    public void Format_NegativeNumbers_ShouldIncludeMinus() =>
        NumberFormatter.Format(-5000).ShouldStartWith("-");

    [Theory]
    [InlineData(999.99, "999.99")]
    [InlineData(50000, "50.00 K")]
    [InlineData(2_000_000, "2.00 M")]
    public void Format_LargePercentageValues_ShouldUseAbbreviations(double input, string expected) =>
        NumberFormatter.Format(input).ShouldBe(expected);

    // -----------------------------------------------------------------
    // Defect-1 coverage: non-finite values must never crash the UI or
    // produce garbage like "Infinity D". These are exactly the values
    // that previously surfaced in the wild as "infinity D infinity
    // angels + infinity D% Next +NaN" and caused the Export crash.
    // -----------------------------------------------------------------
    [Fact]
    public void Format_PositiveInfinity_ShouldReturnInfinityGlyph() =>
        NumberFormatter.Format(double.PositiveInfinity).ShouldBe("\u221E");

    [Fact]
    public void Format_NegativeInfinity_ShouldReturnNegativeInfinityGlyph() =>
        NumberFormatter.Format(double.NegativeInfinity).ShouldBe("-\u221E");

    [Fact]
    public void Format_NaN_ShouldReturnQuestionMark() =>
        NumberFormatter.Format(double.NaN).ShouldBe("?");

    // -----------------------------------------------------------------
    // Above the suffix table (1e36+), we fall through to scientific
    // notation with Unicode superscript exponents — so even truly
    // enormous values still produce a short, readable string.
    // -----------------------------------------------------------------
    [Fact]
    public void Format_AtSuffixCap_ShouldStillUseSuffix()
    {
        // Just below the scientific threshold (1e36) — should still
        // produce a "D" (Decillion) suffix, not scientific notation.
        var result = NumberFormatter.Format(5e35);
        result.ShouldEndWith(" D");
    }

    [Fact]
    public void Format_PastSuffixCap_ShouldUseScientificNotation()
    {
        // 7.53e40 — well past the suffix table.
        var result = NumberFormatter.Format(7.53e40);
        // Should contain "× 10" and a superscript exponent.
        result.ShouldContain("\u00D7 10");
        // Mantissa "7.53" should be present.
        result.ShouldContain("7.53");
    }

    [Theory]
    [InlineData(1e40, "10\u2074\u2070")]  // 10⁴⁰
    [InlineData(1e100, "10\u00B9\u2070\u2070")]  // 10¹⁰⁰
    public void Format_VeryLarge_ShouldUseSuperscriptExponent(double input, string expectedExponent) =>
        NumberFormatter.Format(input).ShouldContain(expectedExponent);

    [Fact]
    public void Format_HandlesTheUsersActualBugInput()
    {
        // The exact failure mode the user reported: a 40+ digit number
        // that previously rendered as a wall of digits in the UI. With
        // the new formatter it becomes a compact scientific-notation
        // string. The mantissa's exact digits depend on IEEE 754
        // rounding of the >15-digit input, so the test asserts shape
        // (short, contains " × 10", contains 10⁴² superscript) rather
        // than an exact mantissa.
        var input = 3409258023457023457230495723957904395823045d;
        var result = NumberFormatter.Format(input);

        result.Length.ShouldBeLessThan(20);
        result.ShouldContain("\u00D7 10");
        // 10⁴² — the magnitude of the input, regardless of mantissa rounding.
        result.ShouldContain("10\u2074\u00B2");
        result.ShouldNotContain("Infinity");
        result.ShouldNotContain("NaN");
    }

    // -----------------------------------------------------------------
    // Superscript helper: standalone tests so the rendering of the
    // exponent is decoupled from the rest of the formatter.
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(0, "\u2070")]                // 0  → ⁰
    [InlineData(1, "\u00B9")]                // 1  → ¹
    [InlineData(2, "\u00B2")]                // 2  → ²
    [InlineData(3, "\u00B3")]                // 3  → ³
    [InlineData(9, "\u2079")]                // 9  → ⁹
    [InlineData(10, "\u00B9\u2070")]         // 10 → ¹⁰
    [InlineData(40, "\u2074\u2070")]         // 40 → ⁴⁰
    [InlineData(123, "\u00B9\u00B2\u00B3")]  // 123 → ¹²³
    public void ToSuperscript_ShouldRenderDigits(int input, string expected) =>
        NumberFormatter.ToSuperscript(input).ShouldBe(expected);

    [Fact]
    public void ToSuperscript_Negative_ShouldIncludeMinusSign()
    {
        // -3 → ⁻³
        NumberFormatter.ToSuperscript(-3).ShouldBe("\u207B\u00B3");
    }

    // -----------------------------------------------------------------
    // FormatScientific direct tests — for callers that want scientific
    // notation regardless of magnitude.
    // -----------------------------------------------------------------
    [Fact]
    public void FormatScientific_ZeroShouldBeZero() =>
        NumberFormatter.FormatScientific(0).ShouldBe("0");

    [Fact]
    public void FormatScientific_Infinity_ShouldReturnGlyph() =>
        NumberFormatter.FormatScientific(double.PositiveInfinity).ShouldBe("\u221E");

    [Fact]
    public void FormatScientific_NaN_ShouldReturnQuestionMark() =>
        NumberFormatter.FormatScientific(double.NaN).ShouldBe("?");

    [Fact]
    public void FormatScientific_KnownValue_ShouldMatchExpected()
    {
        // 7.53e40 → "7.53 × 10⁴⁰"
        var result = NumberFormatter.FormatScientific(7.53e40);
        result.ShouldBe($"7.53 \u00D7 10\u2074\u2070");
    }
}
