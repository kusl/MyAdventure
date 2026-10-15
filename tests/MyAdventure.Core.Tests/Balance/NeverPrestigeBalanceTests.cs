using Shouldly;

namespace MyAdventure.Core.Tests.Balance;

public class NeverPrestigeBalanceTests
{
    private const double Hour = 3600.0;
    private const double Day = 24 * Hour;

    private static readonly BalanceReport Report =
        new BalanceBot(prestigeAtIncomeMultiplier: 0).Play(7 * Day, sampleAt: [3 * Hour, Day, 7 * Day]);

    private static double IncomeAt(double seconds) =>
        Report.IncomeSamples.First(s => s.AtSeconds >= seconds).IncomeLog10;

    [Fact]
    public void Income_KeepsGrowingWithoutPrestige()
    {
        Report.IncomeSamples.Count.ShouldBe(3);
        (IncomeAt(Day) - IncomeAt(3 * Hour)).ShouldBeGreaterThan(1.0);
        (IncomeAt(7 * Day) - IncomeAt(Day)).ShouldBeGreaterThan(0.3);
    }

    [Fact]
    public void FirstDay_ReachesTrillionsPerSecond() =>
        IncomeAt(Day).ShouldBeGreaterThan(12.0);
}
