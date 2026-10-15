using MyAdventure.Core.Entities;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class SpeedMilestoneTests
{
    [Fact]
    public void CycleTimeMultiplier_ZeroOwned_IsExactlyOne() =>
        SpeedMilestone.CalculateCycleTimeMultiplier(0).ShouldBe(1.0);

    [Fact]
    public void CycleTimeMultiplier_Below25_IsExactlyOne()
    {
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
        biz.CycleTimeSeconds.ShouldBe(0.3, tolerance: 1e-12);
    }

    [Fact]
    public void Business_CycleTime_At100_IsEighthBaseTime()
    {
        var biz = MakeBiz(owned: 100, baseTime: 0.6);
        biz.CycleTimeSeconds.ShouldBe(0.075, tolerance: 1e-12);
    }

    [Fact]
    public void Business_CycleTime_At400_IsSixtyFourthBaseTime()
    {
        var biz = MakeBiz(owned: 400, baseTime: 0.6);
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

    [Fact]
    public void Business_RevenuePerSecond_CompoundsRevenueAndSpeedAt100()
    {
        var biz = MakeBiz(owned: 100, baseTime: 1.0);
        biz.RevenuePerSecond.ToDouble().ShouldBe(6400.0 * biz.Efficiency.ToDouble(), tolerance: 1e-6);
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
