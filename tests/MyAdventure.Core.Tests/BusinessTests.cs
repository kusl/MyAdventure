using MyAdventure.Core.Entities;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class BusinessTests
{
    [Fact]
    public void NextCost_ShouldScaleWithOwned()
    {
        var biz = new Business
        {
            Id = "test", Name = "Test", Icon = "T", Color = "#FFF",
            BaseCost = 100, BaseRevenue = 10, BaseTimeSeconds = 1,
            CostMultiplier = 1.1
        };

        biz.NextCost.ShouldBe(100); // 0 owned

        biz.Owned = 10;
        biz.NextCost.ShouldBeGreaterThan(250); // 100 * 1.1^10 ≈ 259
    }

    [Fact]
    public void Revenue_ShouldScaleWithOwned()
    {
        var biz = new Business
        {
            Id = "test", Name = "Test", Icon = "T", Color = "#FFF",
            BaseCost = 100, BaseRevenue = 10, BaseTimeSeconds = 1,
            CostMultiplier = 1.1
        };

        biz.Revenue.ShouldBe(0); // 0 owned
        biz.Owned = 5;
        biz.Revenue.ShouldBe(50);
    }

    [Fact]
    public void Definitions_ShouldReturn6Businesses() =>
        BusinessDefinitions.CreateDefaults().Count.ShouldBe(6);
}
