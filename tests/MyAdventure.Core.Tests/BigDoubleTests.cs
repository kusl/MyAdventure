using MyAdventure.Core.Numerics;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// Tests for <see cref="BigDouble"/>. Every behavior the GameEngine
/// depends on is asserted here: normalization invariants, arithmetic
/// edge cases (signs, zero, infinity, NaN), exponentiation past the
/// double overflow point, and string round-tripping.
/// </summary>
public class BigDoubleTests
{
    // ---------------------------------------------------------------
    // Construction & normalization
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 1.0, 0)]
    [InlineData(10.0, 1.0, 1)]
    [InlineData(100.0, 1.0, 2)]
    [InlineData(1000.0, 1.0, 3)]
    [InlineData(5.0, 5.0, 0)]
    [InlineData(0.5, 5.0, -1)]
    [InlineData(0.01, 1.0, -2)]
    [InlineData(123.456, 1.23456, 2)]
    public void Construct_FromDouble_NormalizesToMantissaInOneToTen(double input, double expMantissa, long expExponent)
    {
        var bd = new BigDouble(input);
        bd.Mantissa.ShouldBe(expMantissa, tolerance: 1e-12);
        bd.Exponent.ShouldBe(expExponent);
    }

    [Fact]
    public void Construct_FromZero_HasZeroMantissaAndZeroExponent()
    {
        var bd = new BigDouble(0.0);
        bd.Mantissa.ShouldBe(0.0);
        bd.Exponent.ShouldBe(0);
        bd.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Construct_FromNaN_PropagatesNaN()
    {
        var bd = new BigDouble(double.NaN);
        bd.IsNaN.ShouldBeTrue();
    }

    [Fact]
    public void Construct_FromInfinity_PropagatesInfinity()
    {
        new BigDouble(double.PositiveInfinity).IsInfinity.ShouldBeTrue();
        new BigDouble(double.NegativeInfinity).IsInfinity.ShouldBeTrue();
        new BigDouble(double.NegativeInfinity).Sign.ShouldBeLessThan(0);
    }

    [Fact]
    public void Construct_FromNegative_PreservesSign()
    {
        var bd = new BigDouble(-12345.0);
        bd.Sign.ShouldBeLessThan(0);
        bd.Mantissa.ShouldBe(-1.2345, tolerance: 1e-12);
        bd.Exponent.ShouldBe(4);
    }

    [Fact]
    public void Construct_FromMantissaExponent_RenormalizesIfOutsideRange()
    {
        // Mantissa 50 with exponent 5 represents 5e6, should normalize to (5.0, 6).
        var bd = new BigDouble(50.0, 5);
        bd.Mantissa.ShouldBe(5.0, tolerance: 1e-12);
        bd.Exponent.ShouldBe(6);
    }

    [Fact]
    public void Construct_FromZeroMantissa_NormalizesToCanonicalZero()
    {
        // (0, 100) should become (0, 0).
        var bd = new BigDouble(0.0, 100);
        bd.IsZero.ShouldBeTrue();
        bd.Exponent.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Addition / subtraction
    // ---------------------------------------------------------------

    [Fact]
    public void Add_TwoSmallValues_IsExact()
    {
        var result = new BigDouble(5.0) + new BigDouble(3.0);
        result.ToDouble().ShouldBe(8.0);
    }

    [Fact]
    public void Add_LargeAndSmall_AbsorbsSmall()
    {
        // 1e100 + 1 ≈ 1e100 (the 1 is below double precision)
        var big = new BigDouble(1.0, 100);
        var small = new BigDouble(1.0);
        var result = big + small;
        result.ShouldBe(big);
    }

    [Fact]
    public void Add_Negation_ProducesZero()
    {
        var a = new BigDouble(5.0, 100);
        var b = -a;
        (a + b).IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Subtract_FromZero_IsNegation()
    {
        var bd = new BigDouble(42.0);
        (BigDouble.Zero - bd).ShouldBe(-bd);
    }

    [Fact]
    public void Add_BothNegative_StaysNegative()
    {
        var result = new BigDouble(-5.0) + new BigDouble(-3.0);
        result.ToDouble().ShouldBe(-8.0);
    }

    [Fact]
    public void Add_NaN_PropagatesNaN()
    {
        (BigDouble.NaN + new BigDouble(5)).IsNaN.ShouldBeTrue();
        (new BigDouble(5) + BigDouble.NaN).IsNaN.ShouldBeTrue();
    }

    [Fact]
    public void Add_InfinityWithOppositeSign_IsNaN()
    {
        (BigDouble.PositiveInfinity + BigDouble.NegativeInfinity).IsNaN.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Multiplication / division
    // ---------------------------------------------------------------

    [Fact]
    public void Multiply_TwoNormalValues_IsExact()
    {
        var result = new BigDouble(3.0) * new BigDouble(4.0);
        result.ToDouble().ShouldBe(12.0);
    }

    [Fact]
    public void Multiply_PastDoubleOverflow_StaysFinite()
    {
        // 1e200 × 1e200 = 1e400 — overflows double, finite in BigDouble.
        var a = new BigDouble(1.0, 200);
        var b = new BigDouble(1.0, 200);
        var result = a * b;
        result.IsFinite.ShouldBeTrue();
        result.Exponent.ShouldBe(400);
        result.Mantissa.ShouldBe(1.0, tolerance: 1e-12);
    }

    [Fact]
    public void Multiply_ZeroAndAnything_IsZero()
    {
        (BigDouble.Zero * new BigDouble(1e100)).IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Multiply_ZeroAndInfinity_IsNaN()
    {
        (BigDouble.Zero * BigDouble.PositiveInfinity).IsNaN.ShouldBeTrue();
    }

    [Fact]
    public void Multiply_PreservesSign()
    {
        (new BigDouble(-3) * new BigDouble(4)).ToDouble().ShouldBe(-12);
        (new BigDouble(-3) * new BigDouble(-4)).ToDouble().ShouldBe(12);
    }

    [Fact]
    public void Divide_NormalValues_IsExact()
    {
        var result = new BigDouble(20.0) / new BigDouble(4.0);
        result.ToDouble().ShouldBe(5.0);
    }

    [Fact]
    public void Divide_ByZero_IsInfinity()
    {
        var result = new BigDouble(5) / BigDouble.Zero;
        result.IsInfinity.ShouldBeTrue();
        result.Sign.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Divide_ZeroByZero_IsNaN()
    {
        (BigDouble.Zero / BigDouble.Zero).IsNaN.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Pow / Sqrt / Log10
    // ---------------------------------------------------------------

    [Fact]
    public void Pow_ZeroExponent_IsOne()
    {
        new BigDouble(1e200).Pow(0).ShouldBe(BigDouble.One);
    }

    [Fact]
    public void Pow_OneExponent_IsIdentity()
    {
        var bd = new BigDouble(7.5, 42);
        bd.Pow(1.0).ShouldBe(bd);
    }

    [Fact]
    public void Pow_PastDoubleOverflow_StaysFinite()
    {
        // 1.11^10000 is Infinity in double; in BigDouble it's around 10^412.
        var result = new BigDouble(1.11).Pow(10_000);
        result.IsFinite.ShouldBeTrue();
        result.Exponent.ShouldBeGreaterThan(400);
        result.Exponent.ShouldBeLessThan(500);
    }

    [Fact]
    public void Pow_SmallBaseLargeExponent_ProducesTinyValue()
    {
        // 0.1^100 = 1e-100
        var result = new BigDouble(0.1).Pow(100);
        result.Exponent.ShouldBe(-100);
        result.Mantissa.ShouldBe(1.0, tolerance: 1e-12);
    }

    [Fact]
    public void Pow_Integer_MatchesDoubleForSmallValues()
    {
        // 2^10 = 1024
        new BigDouble(2.0).Pow(10).ToDouble().ShouldBe(1024.0, tolerance: 1e-9);
    }

    [Fact]
    public void Pow_OnePointZeroSeven_TenThousand_MatchesKnownMagnitude()
    {
        // 1.07^10000 ≈ 10^(10000*log10(1.07)) ≈ 10^293.8
        var result = new BigDouble(1.07).Pow(10_000);
        result.IsFinite.ShouldBeTrue();
        result.Exponent.ShouldBe(293);
    }

    [Fact]
    public void Sqrt_Of100_Is10()
    {
        new BigDouble(100.0).Sqrt().ToDouble().ShouldBe(10.0, tolerance: 1e-9);
    }

    [Fact]
    public void Sqrt_OfHugeValue_StaysFinite()
    {
        // sqrt(10^200) = 10^100
        var result = new BigDouble(1.0, 200).Sqrt();
        result.IsFinite.ShouldBeTrue();
        result.Exponent.ShouldBe(100);
        result.Mantissa.ShouldBe(1.0, tolerance: 1e-9);
    }

    [Fact]
    public void Sqrt_OfZero_IsZero()
    {
        BigDouble.Zero.Sqrt().IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Sqrt_OfNegative_IsNaN()
    {
        new BigDouble(-4).Sqrt().IsNaN.ShouldBeTrue();
    }

    [Fact]
    public void Log10_OfTenToTheHundred_IsHundred()
    {
        new BigDouble(1.0, 100).Log10().ShouldBe(100.0, tolerance: 1e-9);
    }

    [Fact]
    public void Log10_OfOne_IsZero()
    {
        BigDouble.One.Log10().ShouldBe(0.0, tolerance: 1e-12);
    }

    // ---------------------------------------------------------------
    // Comparison
    // ---------------------------------------------------------------

    [Fact]
    public void Compare_LargerExponentIsGreater()
    {
        new BigDouble(1.0, 100).ShouldBeGreaterThan(new BigDouble(9.99, 99));
    }

    [Fact]
    public void Compare_SameExponentComparesByMantissa()
    {
        new BigDouble(5.0, 50).ShouldBeGreaterThan(new BigDouble(3.0, 50));
    }

    [Fact]
    public void Compare_NegativeIsLessThanPositive()
    {
        new BigDouble(-1.0, 100).ShouldBeLessThan(new BigDouble(1.0, 0));
    }

    [Fact]
    public void Compare_ZeroLessThanPositive()
    {
        BigDouble.Zero.ShouldBeLessThan(new BigDouble(1.0));
    }

    [Fact]
    public void Compare_ZeroEqualToZero()
    {
        (BigDouble.Zero == BigDouble.Zero).ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Round-trip serialization
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(1234.5678)]
    [InlineData(1e200)]
    [InlineData(1e-100)]
    public void ToCanonical_ParseRoundTrip_ForRepresentableDoubles(double value)
    {
        var bd = new BigDouble(value);
        var s = bd.ToCanonicalString();
        var parsed = BigDouble.Parse(s);
        parsed.ShouldBe(bd);
    }

    [Fact]
    public void ToCanonical_OfTenToTheThousand_RoundTrips()
    {
        // A value that's impossible to represent as a double.
        var huge = new BigDouble(1.5, 1000);
        var s = huge.ToCanonicalString();
        s.ShouldBe("1.5e1000");
        BigDouble.Parse(s).ShouldBe(huge);
    }

    [Fact]
    public void ToCanonical_OfZero_IsZero()
    {
        BigDouble.Zero.ToCanonicalString().ShouldBe("0");
    }

    [Fact]
    public void Parse_NullOrEmpty_IsZero()
    {
        BigDouble.Parse(null).IsZero.ShouldBeTrue();
        BigDouble.Parse("").IsZero.ShouldBeTrue();
        BigDouble.Parse("   ").IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Parse_LegacyNativeDouble_Works()
    {
        // A value that doesn't have an 'e' separator should still parse.
        BigDouble.Parse("1234.5").ToDouble().ShouldBe(1234.5);
    }

    [Fact]
    public void TryParse_Garbage_ReturnsFalse()
    {
        BigDouble.TryParse("not a number", out _).ShouldBeFalse();
    }

    [Fact]
    public void ToDouble_PastDoubleRange_SaturatesAtMaxValue()
    {
        new BigDouble(1.0, 500).ToDouble().ShouldBe(double.MaxValue);
    }

    [Fact]
    public void ToDouble_WithinDoubleRange_RoundTripsExactly()
    {
        new BigDouble(1234.5).ToDouble().ShouldBe(1234.5);
    }

    // ---------------------------------------------------------------
    // Implicit conversions
    // ---------------------------------------------------------------

    [Fact]
    public void ImplicitConversionFromInt_Works()
    {
        BigDouble bd = 42;
        bd.ToDouble().ShouldBe(42.0);
    }

    [Fact]
    public void ImplicitConversionFromDouble_Works()
    {
        BigDouble bd = 3.14;
        bd.ToDouble().ShouldBe(3.14, tolerance: 1e-12);
    }

    // ---------------------------------------------------------------
    // Floor
    // ---------------------------------------------------------------

    [Fact]
    public void Floor_OfFractional_TruncatesTowardZero()
    {
        new BigDouble(3.7).Floor().ToDouble().ShouldBe(3.0);
    }

    [Fact]
    public void Floor_OfLargeValue_IsIdentity()
    {
        // Past exponent 16, every BigDouble already represents an integer
        // because the mantissa's fractional part is below double precision.
        var bd = new BigDouble(1.5, 50);
        bd.Floor().ShouldBe(bd);
    }
}
