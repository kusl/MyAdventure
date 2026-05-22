using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class BusinessAffordableTests
{
    private Business CreateBusiness(int owned = 0) => new()
    {
        Id = "test",
        Name = "Test",
        Icon = "T",
        Color = "#FFF",
        BaseCost = 100,
        BaseRevenue = 10,
        BaseTimeSeconds = 1,
        CostMultiplier = 1.1,
        Owned = owned
    };

    [Fact]
    public void AffordableCount_NoCash_ShouldBeZero()
    {
        var biz = CreateBusiness();
        biz.AffordableCount(BigDouble.Zero).ShouldBe(0);
    }

    [Fact]
    public void AffordableCount_ExactlyOneCost_ShouldBeOne()
    {
        var biz = CreateBusiness();
        biz.AffordableCount(new BigDouble(100)).ShouldBe(1);
    }

    [Fact]
    public void AffordableCount_MultiplePurchases()
    {
        var biz = CreateBusiness();
        // Cost 0: 100, Cost 1: 110, Cost 2: 121 → cumulative ≈ 331.
        // Use 332 to be safely above the boundary.
        biz.AffordableCount(new BigDouble(332)).ShouldBe(3);
    }

    [Fact]
    public void AffordableCount_SlightlyUnder_ShouldBeOneLess()
    {
        var biz = CreateBusiness();
        // Can buy 2 for 100 + 110 = 210, use 211 to be safely above the boundary.
        biz.AffordableCount(new BigDouble(211)).ShouldBe(2);
    }

    /// <summary>
    /// Post-BigDouble, "buy max" must work even at astronomical cash levels.
    /// The closed-form geometric-series solver gives an O(1) answer rather
    /// than the prior 10,000-iteration safety-capped loop.
    /// </summary>
    [Fact]
    public void AffordableCount_HugeCash_ReturnsLargeFiniteCount()
    {
        var biz = CreateBusiness();
        // With $1e50 and starting cost $100, multiplier 1.1, we can afford
        // a lot: log10(1 + 1e50 × 0.1 / 100) / log10(1.1) ≈ log10(1e47) / 0.0414
        //       ≈ 47 / 0.0414 ≈ 1135 units
        var count = biz.AffordableCount(new BigDouble(1.0, 50));
        count.ShouldBeGreaterThan(1000);
        count.ShouldBeLessThan(2000);
    }

    [Fact]
    public void Revenue_WithMilestones_ShouldMultiply()
    {
        var biz = CreateBusiness(owned: 25);
        // 25 owned × 10 base revenue × 2.0 milestone = 500
        biz.Revenue.ToDouble().ShouldBe(500);
    }

    [Fact]
    public void RevenuePerSecond_ShouldEqualRevenueOverCycleTime()
    {
        var biz = CreateBusiness(owned: 5);
        var expected = biz.Revenue / new BigDouble(biz.CycleTimeSeconds);
        biz.RevenuePerSecond.ShouldBe(expected);
    }
}
