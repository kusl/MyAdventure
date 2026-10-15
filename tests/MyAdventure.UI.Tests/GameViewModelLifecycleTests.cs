using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class GameViewModelLifecycleTests
{
    private static readonly double FiftyAngelBonus = 1.0 + GameBalance.AngelBonusPerAngel * 50;

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public TestTimeProvider(DateTime utcStart) => _now = new DateTimeOffset(utcStart, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static (GameViewModel vm, GameEngine engine, TestTimeProvider clock, ToastService toasts) MakeVm(double angels = 0)
    {
        var clock = new TestTimeProvider(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));

        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance, clock);
        var toasts = new ToastService();
        var vm = new GameViewModel(engine, NullLogger<GameViewModel>.Instance, toasts, clock);

        if (angels > 0)
            typeof(GameEngine).GetProperty(nameof(GameEngine.AngelInvestors))!
                .GetSetMethod(true)!.Invoke(engine, [new BigDouble(angels)]);
        typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!
            .GetSetMethod(true)!.Invoke(engine, [new BigDouble(1_000_000.0)]);

        engine.BuyBusiness("lemonade");
        engine.BuyManager("lemonade");
        engine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        return (vm, engine, clock, toasts);
    }

    private static double LemonadeRevenue(GameEngine engine) =>
        engine.Businesses.First(b => b.Id == "lemonade").Revenue.ToDouble();

    [Fact]
    public void OnResumed_WithoutPriorSuspend_ShouldNotApplyEarnings()
    {
        var (vm, engine, _, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnResumed();

        engine.Cash.ShouldBe(cashBefore);
    }

    [Fact]
    public void OnResumed_TwiceWithoutSuspend_ShouldNotApplyEarnings()
    {
        var (vm, engine, _, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnResumed();
        vm.OnResumed();

        engine.Cash.ShouldBe(cashBefore);
    }

    [Fact]
    public void OnSuspendedThenOnResumed_ShouldApplyEarningsForGap()
    {
        var (vm, engine, clock, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(10));
        vm.OnResumed();

        var earned = (engine.Cash - cashBefore).ToDouble();
        earned.ShouldBe(1000.0 * LemonadeRevenue(engine), tolerance: 1e-9);
    }

    [Fact]
    public void OnSuspendedThenOnResumed_ShouldApplyAngelBonusOnce()
    {
        var (vm, engine, clock, _) = MakeVm(angels: 50);
        var cashBefore = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromSeconds(60));
        vm.OnResumed();

        var earned = (engine.Cash - cashBefore).ToDouble();
        earned.ShouldBe(100.0 * LemonadeRevenue(engine) * FiftyAngelBonus, tolerance: 1e-7);
    }

    [Fact]
    public void OnResumed_ShouldShowToast_WhenEarningsApplied()
    {
        var (vm, _, clock, toasts) = MakeVm();

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(5));
        vm.OnResumed();

        toasts.ActiveToasts.Count.ShouldBe(1);
        toasts.ActiveToasts[0].Message.ShouldContain("While you were away");
    }

    [Fact]
    public void OnResumed_ShouldNotShowToast_WhenNoEarnings()
    {
        var clock = new TestTimeProvider(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance, clock);
        var toasts = new ToastService();
        var vm = new GameViewModel(engine, NullLogger<GameViewModel>.Instance, toasts, clock);

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(5));
        vm.OnResumed();

        toasts.ActiveToasts.Count.ShouldBe(0);
    }

    [Fact]
    public void OnResumed_TinyGap_ShouldNotApplyEarnings()
    {
        var (vm, engine, clock, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        vm.OnResumed();

        engine.Cash.ShouldBe(cashBefore);
    }

    [Fact]
    public void OnResumed_AfterFirstResume_RequiresNewSuspend()
    {
        var (vm, engine, clock, _) = MakeVm();

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(5));
        vm.OnResumed();

        var cashAfterFirst = engine.Cash;
        clock.Advance(TimeSpan.FromMinutes(5));

        vm.OnResumed();

        engine.Cash.ShouldBe(cashAfterFirst);
    }

    [Fact]
    public void OnResumed_ShouldResetLastTickToCurrentTime()
    {
        var (vm, engine, clock, _) = MakeVm();

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(10));
        vm.OnResumed();

        var cashAfterResume = engine.Cash;

        engine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;
        clock.Advance(TimeSpan.FromMilliseconds(16));
        vm.OnTick();

        engine.Cash.ShouldBe(cashAfterResume);
    }

    [Fact]
    public void OnResumed_ShouldApplyEarningsBeforeRefreshingUi()
    {
        var (vm, engine, clock, _) = MakeVm();
        var cashBeforeResume = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(10));
        vm.OnResumed();

        var expectedCash = cashBeforeResume + new BigDouble(1000.0 * LemonadeRevenue(engine));
        engine.Cash.ToDouble().ShouldBe(expectedCash.ToDouble(), tolerance: 1e-9);

        vm.CashText.ShouldBe($"${NumberFormatter.Format(engine.Cash)}");
    }

    [Fact]
    public void OnSuspended_ShouldRecordTimestamp_AndAllowSubsequentResume()
    {
        var (vm, engine, clock, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromSeconds(30));
        vm.OnResumed();

        engine.Cash.ShouldBeGreaterThan(cashBefore);
    }
}
