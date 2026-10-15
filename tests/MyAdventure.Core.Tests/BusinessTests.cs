using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class BusinessTests
{
    [Fact]
    public void NextCost_ShouldScaleWithOwned()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 100,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1
        };

        biz.NextCost.ToDouble().ShouldBe(100);

        biz.Owned = 10;
        biz.NextCost.ToDouble().ShouldBeGreaterThan(250);
    }

    [Fact]
    public void Revenue_ShouldScaleWithOwned()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 100,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1
        };

        biz.Revenue.IsZero.ShouldBeTrue();
        biz.Owned = 5;
        biz.Revenue.ToDouble().ShouldBe(50 * biz.Efficiency.ToDouble(), tolerance: 1e-9);
    }

    [Fact]
    public void NextCost_AtExtremeOwnership_IsFiniteAndUnclamped()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.11,
            Owned = 10_000
        };

        biz.NextCost.IsFinite.ShouldBeTrue();
        biz.NextCost.Exponent.ShouldBeGreaterThan(200);
    }

    [Fact]
    public void Definitions_ShouldReturn6Businesses() =>
        BusinessDefinitions.CreateDefaults().Count.ShouldBe(6);
}
