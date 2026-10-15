using Shouldly;

namespace MyAdventure.Core.Tests.Balance;

public class LateGameBalanceTests
{
    [Theory]
    [InlineData(40.0)]
    [InlineData(80.0)]
    public void SeededLateGame_PrestigeRunsStayInBand(double lifetimeLog10)
    {
        var report = new BalanceBot(seedLifetimeLog10: lifetimeLog10).Play(horizonSeconds: 30 * 86_400, maxPrestiges: 3);

        report.Prestiges.Count.ShouldBe(3);
        foreach (var p in report.Prestiges)
        {
            p.RunSeconds.ShouldBeInRange(3 * 60.0, 4 * 3600.0, $"prestige #{p.Number}");
            double.IsFinite(p.LifetimeLog10).ShouldBeTrue();
        }
    }
}
