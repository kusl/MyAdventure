using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class GameEngineTests
{
    /// <summary>
    /// AngelBonus at 50 angels under the compound formula. Captured here
    /// once so individual tests don't repeat the literal — and so the
    /// rationale for the value is in one place: <c>1.02^50 ≈ 2.6916</c>,
    /// not 2.0 (which was the linear formula's value).
    /// </summary>
    private static readonly double FiftyAngelBonus = Math.Pow(1.02, 50);

    private readonly IGameStateRepository _repo = Substitute.For<IGameStateRepository>();
    private readonly GameEngine _engine;

    public GameEngineTests()
    {
        _repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));

        _engine = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
    }

    [Fact]
    public async Task LoadAsync_NoSave_ShouldStartFresh()
    {
        await _engine.LoadAsync();
        _engine.Cash.ShouldBe(5.0);
        _engine.Businesses.Count.ShouldBe(6);
    }

    [Fact]
    public async Task BuyBusiness_ShouldDeductCashAndIncrementOwned()
    {
        await _engine.LoadAsync();
        SetCash(100);

        var result = _engine.BuyBusiness("lemonade");
        result.ShouldBeTrue();
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1);
        _engine.Cash.ShouldBeLessThan(100);
    }

    [Fact]
    public async Task BuyBusiness_NotEnoughCash_ShouldFail()
    {
        await _engine.LoadAsync();
        // Starting cash is 5.0 — newspaper costs 60, so this should fail
        _engine.BuyBusiness("newspaper").ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_RunningBusiness_ShouldEarnRevenue()
    {
        await _engine.LoadAsync();
        SetCash(1000);
        _engine.BuyBusiness("lemonade");
        _engine.StartBusiness("lemonade");

        for (var i = 0; i < 100; i++)
            _engine.Tick(0.1);

        _engine.Cash.ShouldBeGreaterThan(990);
    }

    [Fact]
    public async Task Tick_MilestoneBoostedRevenue_ShouldEarnMore()
    {
        await _engine.LoadAsync();
        SetCash(1_000_000);

        // Buy 25 lemonade stands to hit first milestone
        for (var i = 0; i < 25; i++)
            _engine.BuyBusiness("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.Owned.ShouldBe(25);
        lemonade.MilestoneMultiplier.ShouldBe(2.0);

        // Revenue should be base × owned × multiplier × post-milestone scaling.
        // PostMilestoneScaling is exactly 1.0 below 1000 owned, so the
        // pre-cap math is identical to before.
        lemonade.Revenue.ShouldBe(lemonade.BaseRevenue * 25 * 2.0);
    }

    // ---------------------------------------------------------------
    // Bug-1 regression coverage: AngelBonus must apply during live play.
    // ---------------------------------------------------------------
    [Fact]
    public async Task Tick_WithAngels_ShouldApplyAngelBonusToLiveEarnings()
    {
        await _engine.LoadAsync();
        SetCash(1000);
        SetAngels(50); // AngelBonus = 1.02^50 ≈ 2.6916 (compound, not 2.0 linear)

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0; // exactly one cycle ready to settle

        var cashBefore = _engine.Cash;
        _engine.Tick(0.0);

        var earned = _engine.Cash - cashBefore;
        // 1 owned × $1 base × 1.0 milestone × ~2.69 angel bonus ≈ $2.69
        earned.ShouldBe(lemonade.Revenue * FiftyAngelBonus);
        earned.ShouldBe(FiftyAngelBonus);
    }

    [Fact]
    public async Task Tick_NoAngels_ShouldEarnExactlyBaseRevenue()
    {
        // Prevents the inverse mistake: with no angels (bonus = 1.0),
        // the multiplier must not change anything. Under the compound
        // formula 1.02^0 = 1.0, so this invariant still holds.
        await _engine.LoadAsync();
        SetCash(1000);

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0;

        var cashBefore = _engine.Cash;
        _engine.Tick(0.0);

        (_engine.Cash - cashBefore).ShouldBe(lemonade.Revenue);
        _engine.AngelBonus.ShouldBe(1.0);
    }

    [Fact]
    public async Task Tick_AngelsAlsoBoostLifetimeEarnings()
    {
        // Lifetime earnings drive the prestige threshold, so the bonus must
        // count into them too (exactly as it does for cash).
        await _engine.LoadAsync();
        SetCash(1000);
        SetAngels(50); // ~×2.69 compound bonus

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0;

        var ltBefore = _engine.LifetimeEarnings;
        _engine.Tick(0.0);

        (_engine.LifetimeEarnings - ltBefore).ShouldBe(lemonade.Revenue * FiftyAngelBonus);
    }

    [Fact]
    public async Task OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice()
    {
        // Invariant: offline earnings for N cycles == live earnings for N cycles
        // when angels are present. This catches both the original "live missed
        // the bonus" bug and the inverse "offline applies it twice" bug.
        var pastTime = DateTime.UtcNow.AddSeconds(-60);
        var savedState = new GameState
        {
            Cash = 0,
            LifetimeEarnings = 0,
            AngelInvestors = 50, // ~×2.69 compound bonus
            BusinessDataJson = """{"lemonade":1}""",
            ManagerDataJson = """{"lemonade":true}""",
            LastPlayedAt = pastTime,
            UpdatedAt = pastTime
        };

        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(savedState));

        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync();

        // Lemonade: cycle 0.6s, base revenue $1, 1 owned, no milestones.
        // ~60s / 0.6s ≈ 100 cycles. Per-cycle revenue = $1 (pre-bonus).
        // With ~×2.69 bonus applied once: 100 × 1 × 2.6916 ≈ $269.16.
        // The window (255–290) tolerates the small gap between pastTime
        // and "now" plus the difference between integer-cycle live ticks
        // and the continuous offline path.
        var expected = 100.0 * FiftyAngelBonus; // ≈ 269.16
        engine.Cash.ShouldBeInRange(expected - 15, expected + 15);
        engine.AngelBonus.ShouldBe(FiftyAngelBonus);
    }

    // ---------------------------------------------------------------
    // ApplyOfflineEarnings — public API used by both LoadAsync (cold
    // start) and the foreground-resume path on GameViewModel.
    //
    // The same calculation must be reachable from outside the engine
    // because background-resume is detected by the View/lifecycle layer
    // and dispatched into the engine via this method. Drift between
    // this method and LoadAsync's usage of it is what allows the live
    // tick path and the offline path to stay in sync — the Bug-1 /
    // OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice invariant
    // depends on there being a single calculation, called from both
    // entry points.
    // ---------------------------------------------------------------
    [Fact]
    public async Task ApplyOfflineEarnings_ShouldAddToCashAndLifetime()
    {
        await _engine.LoadAsync();

        // Set up a managed lemonade stand so it's eligible for offline.
        SetCash(1_000_000);
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var cashBefore = _engine.Cash;
        var ltBefore = _engine.LifetimeEarnings;

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        earned.ShouldBeGreaterThan(0);
        (_engine.Cash - cashBefore).ShouldBe(earned);
        (_engine.LifetimeEarnings - ltBefore).ShouldBe(earned);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NoManagedBusinesses_ShouldReturnZero()
    {
        // Owned but no manager: live play earns from this business, but
        // offline play does not. Nothing to apply in this scenario.
        await _engine.LoadAsync();
        SetCash(1000);
        _engine.BuyBusiness("lemonade"); // owned but no manager

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromMinutes(10));

        earned.ShouldBe(0);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NoBusinessesOwned_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromMinutes(10));
        earned.ShouldBe(0);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_TinyGap_ShouldReturnZero()
    {
        // Below the 1-second threshold the engine treats the gap as
        // "no gap at all" — this protects callers from accidentally
        // double-counting against the live tick loop.
        await _engine.LoadAsync();
        SetCash(1_000_000);
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        _engine.ApplyOfflineEarnings(TimeSpan.FromMilliseconds(500)).ShouldBe(0);
        _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(1)).ShouldBe(0);
        _engine.ApplyOfflineEarnings(TimeSpan.Zero).ShouldBe(0);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NegativeGap_ShouldReturnZero()
    {
        // Defensive: clock skew or test-clock weirdness must never
        // award negative earnings (which would corrupt cash/lifetime).
        await _engine.LoadAsync();
        SetCash(1_000_000);
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var cashBefore = _engine.Cash;
        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(-30));

        earned.ShouldBe(0);
        _engine.Cash.ShouldBe(cashBefore);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_AppliesAngelBonus()
    {
        await _engine.LoadAsync();
        SetCash(1_000_000);
        SetAngels(50); // ~×2.69 compound bonus
        _engine.BuyBusiness("lemonade"); // 1 owned
        _engine.BuyManager("lemonade");

        // Drain the auto-start cycle's progress so we measure offline only.
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.ProgressPercent = 0;

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        // 60s / 0.6s cycle = 100 cycles × $1 base × ~2.69 bonus ≈ $269.16.
        earned.ShouldBe(100.0 * FiftyAngelBonus);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_AndLiveTick_AreEquivalent()
    {
        // Strong invariant: applying offline earnings for N seconds must
        // yield the same amount as ticking the engine for N seconds with
        // the same managed setup. This is what the bug fix relies on:
        // resuming from background must compensate the player as if the
        // tick loop had been running the whole time.
        var repoOffline = Substitute.For<IGameStateRepository>();
        repoOffline.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var offlineEngine = new GameEngine(repoOffline, NullLogger<GameEngine>.Instance);
        await offlineEngine.LoadAsync();
        SetCashOn(offlineEngine, 1_000_000);
        offlineEngine.BuyBusiness("lemonade");
        offlineEngine.BuyManager("lemonade");
        offlineEngine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        var repoLive = Substitute.For<IGameStateRepository>();
        repoLive.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var liveEngine = new GameEngine(repoLive, NullLogger<GameEngine>.Instance);
        await liveEngine.LoadAsync();
        SetCashOn(liveEngine, 1_000_000);
        liveEngine.BuyBusiness("lemonade");
        liveEngine.BuyManager("lemonade");
        liveEngine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        var cashBeforeOffline = offlineEngine.Cash;
        var cashBeforeLive = liveEngine.Cash;

        // 60 seconds offline.
        offlineEngine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        // 60 seconds of 0.1s ticks — 600 ticks. (Smaller deltas keep the
        // floating-point progress accumulation closer to ideal.)
        for (var i = 0; i < 600; i++) liveEngine.Tick(0.1);

        var earnedOffline = offlineEngine.Cash - cashBeforeOffline;
        var earnedLive = liveEngine.Cash - cashBeforeLive;

        // Live tick uses integer cycle counting which can leave a small
        // residual fraction of a cycle in ProgressPercent. Tolerate up to
        // one cycle of revenue ($1 in this setup) of difference.
        Math.Abs(earnedOffline - earnedLive).ShouldBeLessThan(1.5);
    }

    [Fact]
    public async Task Prestige_NotEnoughEarnings_ShouldFail()
    {
        await _engine.LoadAsync();
        var (_, success) = _engine.Prestige();
        success.ShouldBeFalse();
    }

    [Fact]
    public void CalculateAngels_ShouldReturnZeroBelowThreshold() =>
        GameEngine.CalculateAngels(1e11).ShouldBe(0);

    [Fact]
    public void CalculateAngels_ShouldReturnPositiveAboveThreshold() =>
        GameEngine.CalculateAngels(1e14).ShouldBeGreaterThan(0);

    [Fact]
    public async Task ExportToString_ShouldReturnBase64()
    {
        await _engine.LoadAsync();
        SetCash(42.5);

        var exported = _engine.ExportToString();

        exported.ShouldNotBeNullOrWhiteSpace();
        // Should be valid Base64
        var bytes = Convert.FromBase64String(exported);
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        json.ShouldContain("\"cash\"");
        json.ShouldContain("42.5");
    }

    [Fact]
    public async Task ImportFromString_ShouldRestoreState()
    {
        await _engine.LoadAsync();
        SetCash(9999);

        // Buy some businesses
        for (var i = 0; i < 5; i++)
            _engine.BuyBusiness("lemonade");

        _engine.BuyManager("lemonade");

        var exported = _engine.ExportToString();

        // Reset engine by loading fresh
        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync();
        engine2.Cash.ShouldBe(5.0); // fresh start

        // Import the saved state
        var result = engine2.ImportFromString(exported);
        result.ShouldBeTrue();
        engine2.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(5);
        engine2.Businesses.First(b => b.Id == "lemonade").HasManager.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportImport_ShouldRoundTrip()
    {
        await _engine.LoadAsync();
        SetCash(12345.67);

        var exported = _engine.ExportToString();
        var result = _engine.ImportFromString(exported);

        result.ShouldBeTrue();
        _engine.Cash.ShouldBe(12345.67);
    }

    [Fact]
    public void ImportFromString_InvalidBase64_ShouldReturnFalse()
    {
        _engine.ImportFromString("not-valid-base64!!!").ShouldBeFalse();
    }

    [Fact]
    public void ImportFromString_InvalidJson_ShouldReturnFalse()
    {
        var bad = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not json"));
        _engine.ImportFromString(bad).ShouldBeFalse();
    }

    [Fact]
    public void ImportFromString_EmptyString_ShouldReturnFalse()
    {
        _engine.ImportFromString("").ShouldBeFalse();
    }

    [Fact]
    public async Task Prestige_ShouldGiveStartingCash()
    {
        await _engine.LoadAsync();

        // Give enough lifetime earnings to prestige
        var ltProp = typeof(GameEngine).GetProperty(nameof(GameEngine.LifetimeEarnings))!;
        ltProp.GetSetMethod(true)!.Invoke(_engine, [1e14]);

        var (angels, success) = _engine.Prestige();
        success.ShouldBeTrue();
        angels.ShouldBeGreaterThan(0);

        // After prestige, player must have $5 to buy first lemonade stand
        _engine.Cash.ShouldBe(5.0);

        // All businesses should be reset
        _engine.Businesses.All(b => b.Owned == 0).ShouldBeTrue();
    }

    [Fact]
    public async Task Prestige_CashShouldCoverFirstLemonade()
    {
        await _engine.LoadAsync();

        var ltProp = typeof(GameEngine).GetProperty(nameof(GameEngine.LifetimeEarnings))!;
        ltProp.GetSetMethod(true)!.Invoke(_engine, [1e14]);

        var (_, success) = _engine.Prestige();
        success.ShouldBeTrue();

        // The first lemonade stand costs $4, and we should have $5
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.NextCost.ShouldBe(4.0);
        _engine.Cash.ShouldBeGreaterThanOrEqualTo(lemonade.NextCost);

        // Player should be able to buy it
        _engine.BuyBusiness("lemonade").ShouldBeTrue();
    }

    [Fact]
    public async Task BuyMultiple_ShouldBuyRequestedCount()
    {
        await _engine.LoadAsync();
        SetCash(1_000_000);

        var bought = _engine.BuyMultiple("lemonade", 10);
        bought.ShouldBe(10);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(10);
    }

    [Fact]
    public async Task BuyMultiple_NotEnoughCash_ShouldBuyPartial()
    {
        await _engine.LoadAsync();
        // Lemonade costs 4 base, 1.07 multiplier
        // With $10 we can buy 2 (cost 0: $4, cost 1: $4.28 = $8.28 total)
        SetCash(10);

        var bought = _engine.BuyMultiple("lemonade", 100);
        bought.ShouldBe(2);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(2);
    }

    [Fact]
    public async Task BuyMultiple_ZeroCount_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(1000);

        var bought = _engine.BuyMultiple("lemonade", 0);
        bought.ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_InvalidBusiness_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(1000);

        var bought = _engine.BuyMultiple("nonexistent", 5);
        bought.ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_WithManager_ShouldAutoStart()
    {
        await _engine.LoadAsync();
        SetCash(1_000_000);

        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        // Stop the business manually for test setup
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = false;

        _engine.BuyMultiple("lemonade", 5);
        lemonade.IsRunning.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Compound angel bonus — explicit values for the new formula.
    // Documents what each angel count is worth so that an accidental
    // revert to the linear formula breaks something specific instead
    // of just shifting the integration-style ranges by a small amount.
    // ---------------------------------------------------------------
    [Fact]
    public void AngelBonus_AtZeroAngels_ShouldBeOne()
    {
        // 1.02^0 = 1.0 — same as the old linear formula at zero angels.
        // This is what protects the no-angels-yet starting experience.
        _engine.AngelBonus.ShouldBe(1.0);
    }

    [Fact]
    public void AngelBonus_Compounds_NotLinear()
    {
        SetAngels(50);
        // Linear formula: 1 + 50*0.02 = 2.00 (the old behavior)
        // Compound formula: 1.02^50 ≈ 2.6916 (the new behavior)
        _engine.AngelBonus.ShouldBeGreaterThan(2.5);
        _engine.AngelBonus.ShouldBeLessThan(2.8);
    }

    [Fact]
    public void AngelBonus_AtLargeAngelCount_StaysFinite()
    {
        // Defensive: a player who already has thousands of angels (e.g.
        // someone who imported a hand-edited save) should still produce
        // a finite, comparable AngelBonus rather than infinity. 1.02^1500
        // is around 8.3×10^12 — large but representable.
        SetAngels(1500);
        _engine.AngelBonus.ShouldBeGreaterThan(1e12);
        double.IsFinite(_engine.AngelBonus).ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // PostMilestoneScaling — the fix that keeps unit purchases past
    // the 1000-unit milestone cap from collapsing into "you'll never
    // afford the next one". Below 1000 owned the multiplier is 1.0
    // (exact equality, not approximate) so all existing balance is
    // preserved.
    // ---------------------------------------------------------------
    [Fact]
    public void PostMilestoneScaling_BelowCap_IsExactlyOne()
    {
        var biz = new Business
        {
            Id = "t", Name = "T", Icon = "T", Color = "#FFF",
            BaseCost = 1, BaseRevenue = 1, BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 999
        };
        biz.PostMilestoneScaling.ShouldBe(1.0);
    }

    [Fact]
    public void PostMilestoneScaling_AtCap_IsExactlyOne()
    {
        var biz = new Business
        {
            Id = "t", Name = "T", Icon = "T", Color = "#FFF",
            BaseCost = 1, BaseRevenue = 1, BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 1000
        };
        biz.PostMilestoneScaling.ShouldBe(1.0);
    }

    [Fact]
    public void PostMilestoneScaling_PastCap_GrowsAsSqrtOfCost()
    {
        // At Owned = 1100, scaling = 1.07^((1100-1000)/2) = 1.07^50 ≈ 29.46.
        // This compensates for the fact that unit 1100 itself costs about
        // 1.07^100 ≈ 868× more than unit 1000.
        var biz = new Business
        {
            Id = "t", Name = "T", Icon = "T", Color = "#FFF",
            BaseCost = 1, BaseRevenue = 1, BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 1100
        };
        biz.PostMilestoneScaling.ShouldBe(Math.Pow(1.07, 50));
    }

    [Fact]
    public void Revenue_BelowCap_DoesNotIncludePostMilestoneScaling()
    {
        // Pre-cap, revenue is exactly base × owned × milestone — the new
        // PostMilestoneScaling factor is identically 1.0, so all existing
        // balance numbers, milestone tests, and player-side intuitions
        // are preserved unchanged.
        var biz = new Business
        {
            Id = "t", Name = "T", Icon = "T", Color = "#FFF",
            BaseCost = 1, BaseRevenue = 5, BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 100 // hits 25/50/100 -> ×8 milestone
        };
        biz.Revenue.ShouldBe(5 * 100 * 8);
    }

    [Fact]
    public void Revenue_PastCap_IncludesPostMilestoneScaling()
    {
        var biz = new Business
        {
            Id = "t", Name = "T", Icon = "T", Color = "#FFF",
            BaseCost = 1, BaseRevenue = 1, BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 1100
        };
        var milestone = biz.MilestoneMultiplier; // ×327,680 (capped at 1000)
        var expected = 1.0 * 1100 * milestone * Math.Pow(1.07, 50);
        biz.Revenue.ShouldBe(expected);
    }

    private void SetCash(double amount) => SetCashOn(_engine, amount);

    private static void SetCashOn(GameEngine engine, double amount)
    {
        var cashProp = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        cashProp.GetSetMethod(true)!.Invoke(engine, [amount]);
    }

    private void SetAngels(double count)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.AngelInvestors))!;
        prop.GetSetMethod(true)!.Invoke(_engine, [count]);
    }
}
