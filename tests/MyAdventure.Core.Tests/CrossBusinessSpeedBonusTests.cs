using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// Pins the cross-business speed bonus curve (Option B). This is the
/// second compounding earnings axis on top of the per-business speed
/// milestones in <see cref="SpeedMilestone"/>:
/// <list type="bullet">
///   <item>The first six thresholds (25, 50, 100, 200, 300, 400) match
///         the per-business speed ladder one-for-one.</item>
///   <item>Past 400, one additional ×2 stack is granted per +100 owned,
///         <b>forever</b> — there is no terminal threshold. Progression
///         continues at every scale.</item>
///   <item>The multiplier is a <see cref="BigDouble"/> revenue multiplier
///         rather than a cycle-time divisor; this is what lets it grow
///         without bound (cycle time in <see cref="double"/> would
///         underflow long before BigDouble runs out of exponent).</item>
/// </list>
/// </summary>
public class CrossBusinessSpeedBonusTests
{
    // ---------------- BonusCount: the integer ladder ----------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(24, 0)]
    [InlineData(25, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 2)]
    [InlineData(99, 2)]
    [InlineData(100, 3)]
    [InlineData(199, 3)]
    [InlineData(200, 4)]
    [InlineData(299, 4)]
    [InlineData(300, 5)]
    [InlineData(399, 5)]
    [InlineData(400, 6)]
    [InlineData(499, 6)]
    [InlineData(500, 7)]
    [InlineData(600, 8)]
    [InlineData(700, 9)]
    [InlineData(1000, 12)]
    [InlineData(10_000, 102)]
    public void BonusCount_MatchesLadder(int minOwned, int expectedCount) =>
        CrossBusinessSpeedBonus.BonusCount(minOwned).ShouldBe(expectedCount);

    [Fact]
    public void BonusCount_NegativeInput_TreatsAsZero()
    {
        // Defensive against corrupted saves — a negative Owned count
        // shouldn't crash the cross-business calculation.
        CrossBusinessSpeedBonus.BonusCount(-1).ShouldBe(0);
        CrossBusinessSpeedBonus.BonusCount(-1000).ShouldBe(0);
    }

    // ---------------- CalculateSpeedMultiplier: 2^count ----------------

    [Fact]
    public void CalculateSpeedMultiplier_BelowFirstThreshold_IsExactlyOne()
    {
        // BigDouble.One must round-trip exactly so the "no cross bonus"
        // case produces a true multiplicative identity in downstream
        // arithmetic (no precision loss propagating through the per-tick
        // earnings multiply).
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(0).ShouldBe(BigDouble.One);
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(24).ShouldBe(BigDouble.One);
    }

    [Theory]
    [InlineData(25, 2.0)]
    [InlineData(50, 4.0)]
    [InlineData(100, 8.0)]
    [InlineData(200, 16.0)]
    [InlineData(300, 32.0)]
    [InlineData(400, 64.0)]
    [InlineData(500, 128.0)]
    [InlineData(600, 256.0)]
    [InlineData(700, 512.0)]
    [InlineData(800, 1024.0)]
    [InlineData(1000, 4096.0)]
    public void CalculateSpeedMultiplier_AtThresholds_IsPow2OfCount(int minOwned, double expected)
    {
        var mult = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(minOwned);
        mult.ToDouble().ShouldBe(expected, tolerance: 1e-9);
    }

    [Fact]
    public void CalculateSpeedMultiplier_AtAbsurdScale_StaysFiniteOrSafelySaturates()
    {
        // At minOwned = 1,000,000 the bonus is 2^10006 — astronomical
        // but well within BigDouble's exponent range (~10^308 base × any
        // exponent power). The result must be either a finite BigDouble
        // OR a saturated PositiveInfinity that SanitizeMoney will catch
        // downstream. NaN or negative would be a serious bug.
        var mult = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(1_000_000);
        mult.IsNaN.ShouldBeFalse();
        mult.Sign.ShouldBe(1);
    }

    [Fact]
    public void CalculateSpeedMultiplier_ContinuesPast400_DoublesEveryHundred()
    {
        // The defining property of Option B: progression doesn't stall at 400.
        // 500 must be exactly 2× of 400. 600 must be exactly 2× of 500. And so on.
        var at400 = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(400).ToDouble();
        var at500 = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(500).ToDouble();
        var at600 = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(600).ToDouble();
        var at700 = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(700).ToDouble();

        (at500 / at400).ShouldBe(2.0, tolerance: 1e-9);
        (at600 / at500).ShouldBe(2.0, tolerance: 1e-9);
        (at700 / at600).ShouldBe(2.0, tolerance: 1e-9);
    }

    // ---------------- NextThreshold ----------------

    [Theory]
    [InlineData(0, 25)]
    [InlineData(24, 25)]
    [InlineData(25, 50)]
    [InlineData(49, 50)]
    [InlineData(50, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 200)]
    [InlineData(199, 200)]
    [InlineData(200, 300)]
    [InlineData(300, 400)]
    [InlineData(400, 500)]
    [InlineData(450, 500)]
    [InlineData(499, 500)]
    [InlineData(500, 600)]
    [InlineData(900, 1000)]
    [InlineData(1000, 1100)]
    public void NextThreshold_MatchesLadder(int minOwned, int expectedNext) =>
        CrossBusinessSpeedBonus.NextThreshold(minOwned).ShouldBe(expectedNext);

    // ---------------- UnitsToNext ----------------

    [Theory]
    [InlineData(0, 25)]
    [InlineData(15, 10)]
    [InlineData(25, 25)]   // at 25, next is 50, so 25 more units needed
    [InlineData(45, 5)]
    [InlineData(100, 100)] // at 100, next is 200, so 100 more
    [InlineData(150, 50)]
    [InlineData(400, 100)]
    [InlineData(450, 50)]
    [InlineData(499, 1)]
    [InlineData(500, 100)]
    public void UnitsToNext_MatchesGapToNextThreshold(int minOwned, int expectedUnits) =>
        CrossBusinessSpeedBonus.UnitsToNext(minOwned).ShouldBe(expectedUnits);

    [Fact]
    public void UnitsToNext_NeverNegative()
    {
        // Defensive: with negative input the calculation could in theory
        // go negative; the contract guarantees non-negative.
        CrossBusinessSpeedBonus.UnitsToNext(-50).ShouldBeGreaterThanOrEqualTo(0);
        CrossBusinessSpeedBonus.UnitsToNext(0).ShouldBeGreaterThanOrEqualTo(0);
        CrossBusinessSpeedBonus.UnitsToNext(int.MaxValue - 1000).ShouldBeGreaterThanOrEqualTo(0);
    }
}
