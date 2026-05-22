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

        biz.NextCost.ToDouble().ShouldBe(100); // 0 owned

        biz.Owned = 10;
        biz.NextCost.ToDouble().ShouldBeGreaterThan(250); // 100 * 1.1^10 ≈ 259
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

        biz.Revenue.IsZero.ShouldBeTrue(); // 0 owned
        biz.Owned = 5;
        biz.Revenue.ToDouble().ShouldBe(50);
    }

    /// <summary>
    /// Post-BigDouble regression: ownership counts and cost multipliers
    /// that previously produced Infinity (Math.Pow(1.11, 10000)) now
    /// produce a finite BigDouble. The hard 1e200 clamp is gone.
    /// </summary>
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
            Owned = 10_000 // raw Math.Pow(1.11, 10000) is Infinity
        };

        biz.NextCost.IsFinite.ShouldBeTrue();
        // 1.11^10000 is around 10^412 — well past the old 1e200 cap.
        biz.NextCost.Exponent.ShouldBeGreaterThan(200);
    }

    [Fact]
    public void Definitions_ShouldReturn6Businesses() =>
        BusinessDefinitions.CreateDefaults().Count.ShouldBe(6);
}
