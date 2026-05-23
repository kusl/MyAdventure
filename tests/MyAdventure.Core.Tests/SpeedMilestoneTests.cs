using MyAdventure.Core.Entities;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// Speed milestones halve cycle time at 100/200/300/400 ownership.
/// These tests pin both the curve and the compounding behavior, plus
/// the invariant that early-game (owned &lt; 100) sees no change from
/// before this feature existed.
/// </summary>
public class SpeedMilestoneTests
{
    [Fact]
    public void CycleTimeMultiplier_ZeroOwned_IsExactlyOne() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(0).ShouldBe(1.0);

    [Fact]
    public void CycleTimeMultiplier_Below100_IsExactlyOne()
    {
        // Critical invariant: no speed scaling kicks in below the first
        // threshold, so every existing test that uses owned < 100 keeps
        // its assumed cycle time.
        SpeedMilestone.CalculateCycleTimeMultiplier(99).ShouldBe(1.0);
        SpeedMilestone.CalculateCycleTimeMultiplier(50).ShouldBe(1.0);
        SpeedMilestone.CalculateCycleTimeMultiplier(25).ShouldBe(1.0);
    }

    [Fact]
    public void CycleTimeMultiplier_At100_IsHalf() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(100).ShouldBe(0.5);

    [Fact]
    public void CycleTimeMultiplier_At200_IsQuarter() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(200).ShouldBe(0.25);

    [Fact]
    public void CycleTimeMultiplier_At300_IsEighth() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(300).ShouldBe(0.125);

    [Fact]
    public void CycleTimeMultiplier_At400_IsSixteenth() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(400).ShouldBe(0.0625);

    [Fact]
    public void CycleTimeMultiplier_At1000_StaysSixteenth()
    {
        // The default table caps at 4 speed milestones (400). Past 400 the
        // multiplier doesn't shrink further — adding more thresholds is a
        // future-balance decision, deliberately not done here to keep the
        // compound speed-up bounded at ×16.
        SpeedMilestone.CalculateCycleTimeMultiplier(1000).ShouldBe(0.0625);
        SpeedMilestone.CalculateCycleTimeMultiplier(10_000).ShouldBe(0.0625);
    }

    [Fact]
    public void SpeedMultiplier_IsReciprocalOfCycleTimeMultiplier()
    {
        SpeedMilestone.CalculateSpeedMultiplier(0).ShouldBe(1.0);
        SpeedMilestone.CalculateSpeedMultiplier(100).ShouldBe(2.0);
        SpeedMilestone.CalculateSpeedMultiplier(200).ShouldBe(4.0);
        SpeedMilestone.CalculateSpeedMultiplier(300).ShouldBe(8.0);
        SpeedMilestone.CalculateSpeedMultiplier(400).ShouldBe(16.0);
    }

    [Fact]
    public void NextSpeedMilestone_At0_IsHundred()
    {
        var next = SpeedMilestone.NextSpeedMilestone(0);
        next.ShouldNotBeNull();
        next.Threshold.ShouldBe(100);
    }

    [Fact]
    public void NextSpeedMilestone_At100_IsTwoHundred()
    {
        var next = SpeedMilestone.NextSpeedMilestone(100);
        next.ShouldNotBeNull();
        next.Threshold.ShouldBe(200);
    }

    [Fact]
    public void NextSpeedMilestone_At400_IsNull() =>
        SpeedMilestone.NextSpeedMilestone(400).ShouldBeNull();

    [Fact]
    public void NextSpeedMilestone_Past400_IsNull() =>
        SpeedMilestone.NextSpeedMilestone(5000).ShouldBeNull();

    // ---------------- Business integration ----------------

    [Fact]
    public void Business_CycleTime_Below100_EqualsBaseTime()
    {
        var biz = MakeBiz(owned: 50, baseTime: 0.6);
        biz.CycleTimeSeconds.ShouldBe(0.6);
    }

    [Fact]
    public void Business_CycleTime_At100_IsHalfBaseTime()
    {
        var biz = MakeBiz(owned: 100, baseTime: 0.6);
        biz.CycleTimeSeconds.ShouldBe(0.3);
    }

    [Fact]
    public void Business_CycleTime_At400_IsSixteenthBaseTime()
    {
        var biz = MakeBiz(owned: 400, baseTime: 0.6);
        // 0.6 / 16 = 0.0375
        biz.CycleTimeSeconds.ShouldBe(0.0375, tolerance: 1e-12);
    }

    [Fact]
    public void Business_SpeedMultiplier_TracksOwnership()
    {
        MakeBiz(owned: 0).SpeedMultiplier.ShouldBe(1.0);
        MakeBiz(owned: 99).SpeedMultiplier.ShouldBe(1.0);
        MakeBiz(owned: 100).SpeedMultiplier.ShouldBe(2.0);
        MakeBiz(owned: 200).SpeedMultiplier.ShouldBe(4.0);
        MakeBiz(owned: 400).SpeedMultiplier.ShouldBe(16.0);
    }

    /// <summary>
    /// The whole point of the speed-milestone feature: revenue per second
    /// gets a multiplicative boost beyond what the revenue milestones
    /// alone provide. At 100 owned, the revenue milestone is ×8 (25, 50,
    /// 100 all firing); the speed milestone adds another ×2 on top,
    /// giving ×16 EPS over baseline.
    /// </summary>
    [Fact]
    public void Business_RevenuePerSecond_CompoundsRevenueAndSpeedAt100()
    {
        var biz = MakeBiz(owned: 100, baseTime: 1.0);
        // Revenue: 100 owned × $1 × ×8 milestone = $800/cycle
        // CycleTime: 1.0s × 0.5 = 0.5s
        // EPS: $800 / 0.5s = $1600/s
        biz.RevenuePerSecond.ToDouble().ShouldBe(1600.0, tolerance: 1e-9);
    }

    private static Business MakeBiz(int owned, double baseTime = 1.0) => new()
    {
        Id = "test",
        Name = "Test",
        Icon = "T",
        Color = "#FFF",
        BaseCost = 100,
        BaseRevenue = 1,
        BaseTimeSeconds = baseTime,
        CostMultiplier = 1.07,
        Owned = owned
    };
}
