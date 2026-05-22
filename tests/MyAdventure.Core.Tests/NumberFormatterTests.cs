using MyAdventure.Core.Numerics;
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
    // Defect-1 coverage retained: non-finite values must never crash
    // the UI or produce garbage.
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

    [Fact]
    public void Format_AtSuffixCap_ShouldStillUseSuffix()
    {
        var result = NumberFormatter.Format(5e35);
        result.ShouldEndWith(" D");
    }

    [Fact]
    public void Format_PastSuffixCap_ShouldUseScientificNotation()
    {
        var result = NumberFormatter.Format(7.53e40);
        result.ShouldContain("\u00D7 10");
        result.ShouldContain("7.53");
    }

    [Theory]
    [InlineData(1e40, "10\u2074\u2070")]
    [InlineData(1e100, "10\u00B9\u2070\u2070")]
    public void Format_VeryLarge_ShouldUseSuperscriptExponent(double input, string expectedExponent) =>
        NumberFormatter.Format(input).ShouldContain(expectedExponent);

    [Fact]
    public void Format_HandlesTheUsersActualBugInput()
    {
        var input = 3409258023457023457230495723957904395823045d;
        var result = NumberFormatter.Format(input);

        result.Length.ShouldBeLessThan(20);
        result.ShouldContain("\u00D7 10");
        result.ShouldContain("10\u2074\u00B2");
        result.ShouldNotContain("Infinity");
        result.ShouldNotContain("NaN");
    }

    // -----------------------------------------------------------------
    // BigDouble overload: handles magnitudes past 1e308 (the double cap),
    // which is the whole point of the BigDouble migration.
    // -----------------------------------------------------------------
    [Fact]
    public void Format_BigDoubleAt10To500_UsesScientificNotation()
    {
        var bd = new BigDouble(1.0, 500);
        var result = NumberFormatter.Format(bd);
        result.ShouldContain("\u00D7 10");
        result.ShouldContain("1.00");
        // Should include "10⁵⁰⁰" = "10\u2075\u2070\u2070".
        result.ShouldContain("10\u2075\u2070\u2070");
    }

    [Fact]
    public void Format_BigDoubleAt10To5000_StillRenders()
    {
        // A magnitude that the prior double-based formatter could never
        // express. This is the post-BigDouble regression check.
        var bd = new BigDouble(2.5, 5000);
        var result = NumberFormatter.Format(bd);
        result.ShouldContain("2.50");
        result.ShouldContain("\u00D7 10");
        // Length stays bounded — should be ~15 chars.
        result.Length.ShouldBeLessThan(20);
    }

    [Fact]
    public void Format_BigDoubleZero_ShouldRender()
    {
        NumberFormatter.Format(BigDouble.Zero).ShouldBe("0.00");
    }

    [Fact]
    public void Format_BigDoubleNaN_ShouldReturnQuestionMark()
    {
        NumberFormatter.Format(BigDouble.NaN).ShouldBe("?");
    }

    [Fact]
    public void Format_BigDoubleInfinity_ShouldReturnGlyph()
    {
        NumberFormatter.Format(BigDouble.PositiveInfinity).ShouldBe("\u221E");
    }

    [Fact]
    public void Format_BigDoubleNegative_ShouldIncludeMinus()
    {
        NumberFormatter.Format(new BigDouble(-1.5, 100)).ShouldStartWith("-");
    }

    [Fact]
    public void Format_BigDoubleMatchesDoubleForSmallValues()
    {
        // Cross-check: the BigDouble overload should produce the same
        // string as the double overload for values within double's range.
        var bd = new BigDouble(12345.67);
        NumberFormatter.Format(bd).ShouldBe(NumberFormatter.Format(12345.67));
    }

    // -----------------------------------------------------------------
    // Superscript helper
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(0, "\u2070")]
    [InlineData(1, "\u00B9")]
    [InlineData(2, "\u00B2")]
    [InlineData(3, "\u00B3")]
    [InlineData(9, "\u2079")]
    [InlineData(10, "\u00B9\u2070")]
    [InlineData(40, "\u2074\u2070")]
    [InlineData(123, "\u00B9\u00B2\u00B3")]
    public void ToSuperscript_ShouldRenderDigits(int input, string expected) =>
        NumberFormatter.ToSuperscript(input).ShouldBe(expected);

    [Fact]
    public void ToSuperscript_Negative_ShouldIncludeMinusSign()
    {
        NumberFormatter.ToSuperscript(-3).ShouldBe("\u207B\u00B3");
    }

    [Fact]
    public void ToSuperscript_LongExponent_RendersAllDigits()
    {
        // BigDouble exponents are longs, not ints. 1000000 in superscript
        // should produce all seven digit positions.
        var result = NumberFormatter.ToSuperscript(1_000_000L);
        result.Length.ShouldBe(7);
    }

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
        var result = NumberFormatter.FormatScientific(7.53e40);
        result.ShouldBe($"7.53 \u00D7 10\u2074\u2070");
    }
}
