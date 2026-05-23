using MyAdventure.Core.Entities;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// Speed milestones halve cycle time at 25/50/100/200/300/400 ownership —
/// the six-threshold AdCap-parity ladder. These tests pin both the curve
/// and the compounding behavior, plus the invariant that very early game
/// (owned &lt; 25) sees no change from before this feature existed. The
/// older four-threshold table (100/200/300/400 → ×16 max) has been
/// replaced; tests targeting the previous ladder were rewritten here.
/// </summary>
public class SpeedMilestoneTests
{
    [Fact]
    public void CycleTimeMultiplier_ZeroOwned_IsExactlyOne() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(0).ShouldBe(1.0);

    [Fact]
    public void CycleTimeMultiplier_Below25_IsExactlyOne()
    {
        // Critical invariant: no speed scaling kicks in below the first
        // threshold. The threshold MOVED from 100 to 25 with the AdCap-
        // parity update, so any existing test using owned in [25, 99]
        // now legitimately sees a speed multiplier where it previously
        // didn't. Tests for that range have been updated in the suite.
        SpeedMilestone.CalculateCycleTimeMultiplier(24).ShouldBe(1.0);
        SpeedMilestone.CalculateCycleTimeMultiplier(10).ShouldBe(1.0);
        SpeedMilestone.CalculateCycleTimeMultiplier(1).ShouldBe(1.0);
    }

    [Fact]
    public void CycleTimeMultiplier_At25_IsHalf() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(25).ShouldBe(0.5);

    [Fact]
    public void CycleTimeMultiplier_At50_IsQuarter() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(50).ShouldBe(0.25);

    [Fact]
    public void CycleTimeMultiplier_At100_IsEighth() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(100).ShouldBe(0.125);

    [Fact]
    public void CycleTimeMultiplier_At200_IsSixteenth() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(200).ShouldBe(0.0625);

    [Fact]
    public void CycleTimeMultiplier_At300_IsThirtySecond() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(300).ShouldBe(0.03125);

    [Fact]
    public void CycleTimeMultiplier_At400_IsSixtyFourth() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(400).ShouldBe(0.015625);

    [Fact]
    public void CycleTimeMultiplier_At1000_StaysSixtyFourth()
    {
        // The default table caps at six speed milestones (400). Past 400 the
        // multiplier doesn't shrink further — the cross-business bonus
        // (CrossBusinessSpeedBonus, Option B) is what keeps progression
        // moving past this ceiling. The per-business cap is intentional;
        // see SpeedMilestone XML docs for why further per-business
        // halvings would underflow cycle time.
        SpeedMilestone.CalculateCycleTimeMultiplier(1000).ShouldBe(0.015625);
        SpeedMilestone.CalculateCycleTimeMultiplier(10_000).ShouldBe(0.015625);
    }

    [Fact]
    public void SpeedMultiplier_IsReciprocalOfCycleTimeMultiplier()
    {
        SpeedMilestone.CalculateSpeedMultiplier(0).ShouldBe(1.0);
        SpeedMilestone.CalculateSpeedMultiplier(24).ShouldBe(1.0);
        SpeedMilestone.CalculateSpeedMultiplier(25).ShouldBe(2.0);
        SpeedMilestone.CalculateSpeedMultiplier(50).ShouldBe(4.0);
        SpeedMilestone.CalculateSpeedMultiplier(100).ShouldBe(8.0);
        SpeedMilestone.CalculateSpeedMultiplier(200).ShouldBe(16.0);
        SpeedMilestone.CalculateSpeedMultiplier(300).ShouldBe(32.0);
        SpeedMilestone.CalculateSpeedMultiplier(400).ShouldBe(64.0);
    }

    [Fact]
    public void NextSpeedMilestone_At0_IsTwentyFive()
    {
        var next = SpeedMilestone.NextSpeedMilestone(0);
        next.ShouldNotBeNull();
        next.Threshold.ShouldBe(25);
    }

    [Fact]
    public void NextSpeedMilestone_At25_IsFifty()
    {
        var next = SpeedMilestone.NextSpeedMilestone(25);
        next.ShouldNotBeNull();
        next.Threshold.ShouldBe(50);
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
    public void Business_CycleTime_Below25_EqualsBaseTime()
    {
        var biz = MakeBiz(owned: 24, baseTime: 0.6);
        biz.CycleTimeSeconds.ShouldBe(0.6);
    }

    [Fact]
    public void Business_CycleTime_At25_IsHalfBaseTime()
    {
        var biz = MakeBiz(owned: 25, baseTime: 0.6);
        // 0.6 × 0.5 = 0.3
        biz.CycleTimeSeconds.ShouldBe(0.3, tolerance: 1e-12);
    }

    [Fact]
    public void Business_CycleTime_At100_IsEighthBaseTime()
    {
        var biz = MakeBiz(owned: 100, baseTime: 0.6);
        // 0.6 / 8 = 0.075
        biz.CycleTimeSeconds.ShouldBe(0.075, tolerance: 1e-12);
    }

    [Fact]
    public void Business_CycleTime_At400_IsSixtyFourthBaseTime()
    {
        var biz = MakeBiz(owned: 400, baseTime: 0.6);
        // 0.6 / 64 = 0.009375
        biz.CycleTimeSeconds.ShouldBe(0.009375, tolerance: 1e-12);
    }

    [Fact]
    public void Business_SpeedMultiplier_TracksOwnership()
    {
        MakeBiz(owned: 0).SpeedMultiplier.ShouldBe(1.0);
        MakeBiz(owned: 24).SpeedMultiplier.ShouldBe(1.0);
        MakeBiz(owned: 25).SpeedMultiplier.ShouldBe(2.0);
        MakeBiz(owned: 50).SpeedMultiplier.ShouldBe(4.0);
        MakeBiz(owned: 100).SpeedMultiplier.ShouldBe(8.0);
        MakeBiz(owned: 200).SpeedMultiplier.ShouldBe(16.0);
        MakeBiz(owned: 300).SpeedMultiplier.ShouldBe(32.0);
        MakeBiz(owned: 400).SpeedMultiplier.ShouldBe(64.0);
    }

    /// <summary>
    /// The whole point of the speed-milestone feature: revenue per second
    /// gets a multiplicative boost beyond what the revenue milestones
    /// alone provide. At 100 owned of a baseTime=1.0 business:
    ///   - Revenue milestones at 25/50/100 fire ×8.
    ///   - Speed milestones at 25/50/100 fire ×8 (cycle time × 0.125).
    /// Compound: revenue = 100 × $1 × 8 = $800/cycle; cycle = 1.0 × 0.125 = 0.125s.
    /// EPS = $800 / 0.125s = $6400/s. Under the old four-threshold table
    /// this was $1600/s — the AdCap-parity update produces 4× higher EPS
    /// at owned=100, which is the intended buff.
    /// </summary>
    [Fact]
    public void Business_RevenuePerSecond_CompoundsRevenueAndSpeedAt100()
    {
        var biz = MakeBiz(owned: 100, baseTime: 1.0);
        biz.RevenuePerSecond.ToDouble().ShouldBe(6400.0, tolerance: 1e-9);
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
