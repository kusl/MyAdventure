using Shouldly;

namespace MyAdventure.Core.Tests.Balance;

public class FreshStartBalanceTests
{
    private static readonly BalanceReport Report = new BalanceBot().Play(horizonSeconds: 86_400, maxPrestiges: 10);

    [Fact]
    public void FirstManager_ArrivesWithinTwoMinutes()
    {
        (Report.FirstManagerSeconds ?? double.PositiveInfinity).ShouldBeLessThan(120.0);
    }

    [Fact]
    public void FirstPrestige_IsWorthDoublingIncome_WithinAnHour()
    {
        Report.Prestiges.Count.ShouldBeGreaterThan(0);
        Report.Prestiges[0].AtSeconds.ShouldBeInRange(8 * 60.0, 60 * 60.0);
    }

    [Fact]
    public void PrestigeLoop_NeitherRunsAwayNorStalls()
    {
        Report.Prestiges.Count.ShouldBe(10);
        foreach (var p in Report.Prestiges)
            p.RunSeconds.ShouldBeInRange(5 * 60.0, 90 * 60.0, $"prestige #{p.Number}");
    }

    [Fact]
    public void EveryPrestige_RaisesLifetimeEarnings()
    {
        for (var i = 1; i < Report.Prestiges.Count; i++)
            Report.Prestiges[i].LifetimeLog10.ShouldBeGreaterThan(Report.Prestiges[i - 1].LifetimeLog10);
    }
}
