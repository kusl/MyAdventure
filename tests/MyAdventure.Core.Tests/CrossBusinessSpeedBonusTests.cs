using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class CrossBusinessSpeedBonusTests
{
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
    [InlineData(500, 6)]
    [InlineData(10_000, 6)]
    public void BonusCount_MatchesLadder(int minOwned, int expectedCount) =>
        CrossBusinessSpeedBonus.BonusCount(minOwned).ShouldBe(expectedCount);

    [Fact]
    public void BonusCount_NegativeInput_TreatsAsZero()
    {
        CrossBusinessSpeedBonus.BonusCount(-1).ShouldBe(0);
        CrossBusinessSpeedBonus.BonusCount(-1000).ShouldBe(0);
    }

    [Fact]
    public void CalculateSpeedMultiplier_BelowFirstThreshold_IsExactlyOne()
    {
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(0).ShouldBe(BigDouble.One);
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(24).ShouldBe(BigDouble.One);
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(50, 2)]
    [InlineData(100, 3)]
    [InlineData(200, 4)]
    [InlineData(300, 5)]
    [InlineData(400, 6)]
    public void CalculateSpeedMultiplier_AtThresholds_CompoundsPerThreshold(int minOwned, int stacks)
    {
        var expected = Math.Pow(GameBalance.CrossBusinessMultiplierPerThreshold, stacks);
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(minOwned).ToDouble().ShouldBe(expected, tolerance: 1e-9);
    }

    [Theory]
    [InlineData(400L)]
    [InlineData(1_000L)]
    [InlineData(1_000_000L)]
    [InlineData(long.MaxValue)]
    public void CalculateSpeedMultiplier_IsBoundedAtMaxMultiplier(long minOwned) =>
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(minOwned).ToDouble()
            .ShouldBe(CrossBusinessSpeedBonus.MaxMultiplier, tolerance: 1e-9);

    [Fact]
    public void MaxMultiplier_IsSmallRelativeToMilestoneContent() =>
        CrossBusinessSpeedBonus.MaxMultiplier.ShouldBeLessThan(16.0);

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
    [InlineData(399, 400)]
    public void NextThreshold_MatchesLadder(int minOwned, int expectedNext) =>
        CrossBusinessSpeedBonus.NextThreshold(minOwned).ShouldBe(expectedNext);

    [Theory]
    [InlineData(400L)]
    [InlineData(10_000L)]
    [InlineData(long.MaxValue)]
    public void NextThreshold_AfterLastThreshold_IsNull(long minOwned) =>
        CrossBusinessSpeedBonus.NextThreshold(minOwned).HasValue.ShouldBeFalse();

    [Theory]
    [InlineData(0, 25)]
    [InlineData(15, 10)]
    [InlineData(25, 25)]
    [InlineData(45, 5)]
    [InlineData(100, 100)]
    [InlineData(150, 50)]
    [InlineData(399, 1)]
    [InlineData(400, 0)]
    [InlineData(500, 0)]
    public void UnitsToNext_MatchesGapToNextThreshold(int minOwned, int expectedUnits) =>
        CrossBusinessSpeedBonus.UnitsToNext(minOwned).ShouldBe(expectedUnits);

    [Fact]
    public void UnitsToNext_NeverNegative()
    {
        CrossBusinessSpeedBonus.UnitsToNext(-50).ShouldBeGreaterThanOrEqualTo(0);
        CrossBusinessSpeedBonus.UnitsToNext(0).ShouldBeGreaterThanOrEqualTo(0);
        CrossBusinessSpeedBonus.UnitsToNext(int.MaxValue - 1000).ShouldBeGreaterThanOrEqualTo(0);
    }
}
