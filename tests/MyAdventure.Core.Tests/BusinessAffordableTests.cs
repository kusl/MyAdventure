using MyAdventure.Core.Entities;
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
        biz.AffordableCount(0).ShouldBe(0);
    }

    [Fact]
    public void AffordableCount_ExactlyOneCost_ShouldBeOne()
    {
        var biz = CreateBusiness();
        biz.AffordableCount(100).ShouldBe(1);
    }

    [Fact]
    public void AffordableCount_MultiplePurchases()
    {
        var biz = CreateBusiness();
        // Cost 0: 100, Cost 1: 110, Cost 2: 121 => total ~331
        // Use 332 to avoid IEEE 754 floating point edge (1.1^2 = 1.2100000000000002)
        biz.AffordableCount(332).ShouldBe(3);
    }

    [Fact]
    public void AffordableCount_SlightlyUnder_ShouldBeOneLess()
    {
        var biz = CreateBusiness();
        // Can buy 2 for 100 + 110 = 210, but not 3 (need ~121 more)
        biz.AffordableCount(210).ShouldBe(2);
    }

    [Fact]
    public void Revenue_WithMilestones_ShouldMultiply()
    {
        var biz = CreateBusiness(owned: 25);
        // 25 owned × 10 base revenue × 2.0 milestone = 500
        biz.Revenue.ShouldBe(500);
    }

    [Fact]
    public void RevenuePerSecond_ShouldEqualRevenueOverCycleTime()
    {
        var biz = CreateBusiness(owned: 5);
        var expected = biz.Revenue / biz.CycleTimeSeconds;
        biz.RevenuePerSecond.ShouldBe(expected);
    }
}
