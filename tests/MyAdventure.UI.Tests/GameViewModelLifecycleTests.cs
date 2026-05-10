using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

/// <summary>
/// Tests for <see cref="GameViewModel.OnSuspended"/> and
/// <see cref="GameViewModel.OnResumed"/> — the foreground-resume hooks
/// that compensate the player for time spent with the app suspended in
/// the background. These cover the bug where the
/// <c>DispatcherTimer</c>'s post-resume <c>delta</c> was clamped to 1
/// second, silently discarding the rest of the suspension period.
/// </summary>
public class GameViewModelLifecycleTests
{
    /// <summary>
    /// AngelBonus at 50 angels under the compound formula:
    /// <c>1.02^50 ≈ 2.6916</c>. Centralized here so individual tests
    /// don't repeat the literal and so the rationale is in one place.
    /// </summary>
    private static readonly double FiftyAngelBonus = Math.Pow(1.02, 50);

    /// <summary>
    /// Minimal hand-rolled fake to drive the ViewModel's clock from tests.
    /// Avoids adding a Microsoft.Extensions.TimeProvider.Testing dependency
    /// just for two test files; all we need is settable "now."
    /// </summary>
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

        // Force initial cash & angels via reflection so we don't depend on
        // BuyBusiness side-effects bleeding into earnings calculations.
        if (angels > 0)
            typeof(GameEngine).GetProperty(nameof(GameEngine.AngelInvestors))!
                .GetSetMethod(true)!.Invoke(engine, [angels]);
        typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!
            .GetSetMethod(true)!.Invoke(engine, [1_000_000.0]);

        // Set up a managed lemonade stand so offline earnings have
        // something to compute.
        engine.BuyBusiness("lemonade");
        engine.BuyManager("lemonade");
        // Reset progress so the auto-started cycle doesn't skew results.
        engine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        return (vm, engine, clock, toasts);
    }

    // ---------------------------------------------------------------
    // OnResumed without a prior OnSuspended is a no-op for earnings.
    // This is the cold-start guard: LoadAsync handles the offline gap
    // there, and OnResumed must not double-count.
    // ---------------------------------------------------------------
    [Fact]
    public void OnResumed_WithoutPriorSuspend_ShouldNotApplyEarnings()
    {
        var (vm, engine, _, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnResumed();

        engine.Cash.ShouldBe(cashBefore);
    }

    // ---------------------------------------------------------------
    // The guard must hold across the FULL lifecycle, not just before
    // the first activation event. If lifecycle events ever fire in an
    // unexpected order (Activated without a prior Deactivated), we
    // must not pay out random amounts of money.
    // ---------------------------------------------------------------
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

        // Lemonade: cycle 0.6s, base $1, 1 owned, no angels.
        // 600s / 0.6s = 1000 cycles × $1 × 1.0 bonus = $1000.
        var earned = engine.Cash - cashBefore;
        earned.ShouldBe(1000.0);
    }

    [Fact]
    public void OnSuspendedThenOnResumed_ShouldApplyAngelBonusOnce()
    {
        // Same calculation as above but with angels — must match the
        // canonical OfflineEarnings path (one bonus multiplier, applied
        // once at the end). This is the GameViewModel-level mirror of
        // OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice in the
        // engine tests.
        var (vm, engine, clock, _) = MakeVm(angels: 50); // ~×2.69 compound
        var cashBefore = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromSeconds(60));
        vm.OnResumed();

        // 60s / 0.6s = 100 cycles × $1 × ~2.6916 compound bonus ≈ $269.16.
        // Tolerance accounts for IEEE 754 ordering between the engine's
        // accumulation path and the test's (100 * bonus) reference value.
        var earned = engine.Cash - cashBefore;
        earned.ShouldBe(100.0 * FiftyAngelBonus, tolerance: 1e-7);
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
        // No managed business + a suspend/resume cycle -> no earnings,
        // no toast. The "while you were away" message would be misleading
        // when nothing was actually earned.
        var clock = new TestTimeProvider(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance, clock);
        var toasts = new ToastService();
        var vm = new GameViewModel(engine, NullLogger<GameViewModel>.Instance, toasts, clock);

        // No managed businesses; offline path returns 0.
        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(5));
        vm.OnResumed();

        toasts.ActiveToasts.Count.ShouldBe(0);
    }

    [Fact]
    public void OnResumed_TinyGap_ShouldNotApplyEarnings()
    {
        // Sub-1-second resume gap (e.g. screen flicker, ultra-brief lock)
        // is below the engine's MinimumOfflineGapSeconds threshold and
        // must produce no earnings. This protects against double-counting
        // the live tick loop.
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
        // After OnResumed clears _suspendedAt, a SECOND OnResumed call
        // (without an intervening OnSuspended) must not pay out again.
        // This covers the case where the activatable lifetime fires
        // duplicate Activated events (rare but known on some platforms).
        var (vm, engine, clock, _) = MakeVm();

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(5));
        vm.OnResumed();

        var cashAfterFirst = engine.Cash;
        clock.Advance(TimeSpan.FromMinutes(5));

        vm.OnResumed(); // no new suspend -> no new payout

        engine.Cash.ShouldBe(cashAfterFirst);
    }

    [Fact]
    public void OnResumed_ShouldResetLastTickToCurrentTime()
    {
        // After resume, the very next OnTick must NOT see a multi-minute
        // delta. _lastTick has to be reset to "now" inside OnResumed so
        // the resume gap and the live tick path don't overlap.
        var (vm, engine, clock, _) = MakeVm();

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(10));
        vm.OnResumed();

        var cashAfterResume = engine.Cash;

        // Now simulate the very next tick: delta should be ~0, so
        // running OnTick must add at most ~one cycle of earnings, not
        // ten minutes' worth. (We start the lemonade fresh at 0% so a
        // sub-cycle delta produces no payout.)
        engine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;
        clock.Advance(TimeSpan.FromMilliseconds(16));
        vm.OnTick();

        // 16ms / 600ms = 2.67% progress. No cycle should have completed,
        // so cash is unchanged.
        engine.Cash.ShouldBe(cashAfterResume);
    }

    [Fact]
    public void OnResumed_ShouldApplyEarningsBeforeRefreshingUi()
    {
        // The CashText binding has to reflect the new cash on resume —
        // not require the next tick to update. Without this, the player
        // would see their old cash for ~16ms after resume (visible flicker).
        var (vm, engine, clock, _) = MakeVm();

        // Pre-suspend cash is whatever MakeVm left after buying the
        // lemonade ($4) and its manager ($4,000). What matters is that
        // CashText after resume reflects post-payout, not pre-payout.
        var cashBeforeResume = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromMinutes(10));
        vm.OnResumed();

        // 10 minutes / 0.6s cycle = 1000 cycles × $1 × 1.0 bonus = $1,000.
        engine.Cash.ShouldBe(cashBeforeResume + 1000.0);

        // CashText must be a fresh format of the post-payout cash, not
        // a stale snapshot taken before OnSuspended saved.
        vm.CashText.ShouldBe($"${NumberFormatter.Format(engine.Cash)}");
    }

    [Fact]
    public void OnSuspended_ShouldRecordTimestamp_AndAllowSubsequentResume()
    {
        // The most basic invariant: OnSuspended records something, and
        // a subsequent OnResumed pays out. Without the timestamp being
        // captured, OnResumed would see _suspendedAt == null and skip
        // the payout — effectively the bug.
        var (vm, engine, clock, _) = MakeVm();
        var cashBefore = engine.Cash;

        vm.OnSuspended();
        clock.Advance(TimeSpan.FromSeconds(30));
        vm.OnResumed();

        engine.Cash.ShouldBeGreaterThan(cashBefore);
    }
}
