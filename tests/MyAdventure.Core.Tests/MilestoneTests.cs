using MyAdventure.Core.Entities;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class MilestoneTests
{
    [Fact]
    public void CalculateMultiplier_ZeroOwned_ShouldBe1()
    {
        Milestone.CalculateMultiplier(0).ShouldBe(1.0);
    }

    [Fact]
    public void CalculateMultiplier_Below25_ShouldBe1()
    {
        Milestone.CalculateMultiplier(24).ShouldBe(1.0);
    }

    [Fact]
    public void CalculateMultiplier_At25_ShouldBe2()
    {
        Milestone.CalculateMultiplier(25).ShouldBe(2.0);
    }

    [Fact]
    public void CalculateMultiplier_At50_ShouldBe4()
    {
        // 25 milestone (×2) × 50 milestone (×2) = ×4
        Milestone.CalculateMultiplier(50).ShouldBe(4.0);
    }

    [Fact]
    public void CalculateMultiplier_At100_ShouldBe8()
    {
        // ×2 × ×2 × ×2 = ×8
        Milestone.CalculateMultiplier(100).ShouldBe(8.0);
    }

    [Fact]
    public void CalculateMultiplier_At1000_ShouldBeMax()
    {
        // All milestones: 2^6 × 4^5 × 5 = 64 × 1024 × 5 = 327680
        var result = Milestone.CalculateMultiplier(1000);
        result.ShouldBe(64 * 1024 * 5.0);
    }

    [Fact]
    public void NextMilestone_At0_ShouldBe25()
    {
        var next = Milestone.NextMilestone(0);
        next.ShouldNotBeNull();
        next.Threshold.ShouldBe(25);
    }

    [Fact]
    public void NextMilestone_At25_ShouldBe50()
    {
        var next = Milestone.NextMilestone(25);
        next.ShouldNotBeNull();
        next.Threshold.ShouldBe(50);
    }

    [Fact]
    public void NextMilestone_At1000_ShouldBeNull()
    {
        Milestone.NextMilestone(1000).ShouldBeNull();
    }

    [Fact]
    public void UnitsToNext_At0_ShouldBe25()
    {
        Milestone.UnitsToNext(0).ShouldBe(25);
    }

    [Fact]
    public void UnitsToNext_At20_ShouldBe5()
    {
        Milestone.UnitsToNext(20).ShouldBe(5);
    }

    [Fact]
    public void UnitsToNext_At1000_ShouldBe0()
    {
        Milestone.UnitsToNext(1000).ShouldBe(0);
    }
}
