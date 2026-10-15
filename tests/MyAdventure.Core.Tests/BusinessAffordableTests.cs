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
        biz.AffordableCount(new BigDouble(332)).ShouldBe(3);
    }

    [Fact]
    public void AffordableCount_SlightlyUnder_ShouldBeOneLess()
    {
        var biz = CreateBusiness();
        biz.AffordableCount(new BigDouble(211)).ShouldBe(2);
    }

    [Fact]
    public void AffordableCount_HugeCash_ReturnsLargeFiniteCount()
    {
        var biz = CreateBusiness();
        var count = biz.AffordableCount(new BigDouble(1.0, 50));
        count.ShouldBeGreaterThan(1000);
        count.ShouldBeLessThan(2000);
    }

    [Fact]
    public void Revenue_WithMilestones_ShouldMultiply()
    {
        var biz = CreateBusiness(owned: 25);
        biz.Revenue.ToDouble().ShouldBe(10 * 25 * 2 * biz.Efficiency.ToDouble(), tolerance: 1e-9);
    }

    [Fact]
    public void RevenuePerSecond_ShouldEqualRevenueOverCycleTime()
    {
        var biz = CreateBusiness(owned: 5);
        var expected = biz.Revenue / new BigDouble(biz.CycleTimeSeconds);
        biz.RevenuePerSecond.ShouldBe(expected);
    }
}
