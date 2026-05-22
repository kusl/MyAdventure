using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// GameEngine tests. Post-BigDouble migration: all monetary and angel
/// values are <see cref="BigDouble"/> and have no practical ceiling. The
/// tests previously written against <c>double</c> with explicit 1e200
/// clamps are lifted to BigDouble here; the invariants they test
/// (live ↔ offline equivalence, prestige reset, angel bonus, etc.)
/// remain identical.
/// </summary>
public class GameEngineTests
{
    /// <summary>
    /// AngelBonus at 50 angels under the compound formula: 1.02^50 ≈ 2.6916.
    /// Centralized to avoid repeating the literal across tests.
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
        _engine.Cash.ToDouble().ShouldBe(5.0);
        _engine.Businesses.Count.ShouldBe(6);
    }

    [Fact]
    public async Task BuyBusiness_ShouldDeductCashAndIncrementOwned()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(100));

        var result = _engine.BuyBusiness("lemonade");
        result.ShouldBeTrue();
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1);
        _engine.Cash.ShouldBeLessThan(new BigDouble(100));
    }

    [Fact]
    public async Task BuyBusiness_NotEnoughCash_ShouldFail()
    {
        await _engine.LoadAsync();
        // Starting cash is 5.0 — newspaper costs 60, so this should fail.
        _engine.BuyBusiness("newspaper").ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_RunningBusiness_ShouldEarnRevenue()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));
        _engine.BuyBusiness("lemonade");
        _engine.StartBusiness("lemonade");

        for (var i = 0; i < 100; i++)
            _engine.Tick(0.1);

        _engine.Cash.ShouldBeGreaterThan(new BigDouble(990));
    }

    [Fact]
    public async Task Tick_MilestoneBoostedRevenue_ShouldEarnMore()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));

        for (var i = 0; i < 25; i++)
            _engine.BuyBusiness("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.Owned.ShouldBe(25);
        lemonade.MilestoneMultiplier.ShouldBe(2.0);
        lemonade.Revenue.ToDouble().ShouldBe(lemonade.BaseRevenue * 25 * 2.0);
    }

    // ---------------- Angel bonus on live earnings ----------------

    [Fact]
    public async Task Tick_WithAngels_ShouldApplyAngelBonusToLiveEarnings()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));
        SetAngels(new BigDouble(50));

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0;

        var cashBefore = _engine.Cash;
        _engine.Tick(0.0);

        var earned = (_engine.Cash - cashBefore).ToDouble();
        // 1 owned × $1 base × 1.0 milestone × ~2.69 angel bonus ≈ $2.69.
        earned.ShouldBe(FiftyAngelBonus, tolerance: 1e-9);
    }

    [Fact]
    public async Task Tick_NoAngels_ShouldEarnExactlyBaseRevenue()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0;

        var cashBefore = _engine.Cash;
        _engine.Tick(0.0);

        (_engine.Cash - cashBefore).ToDouble().ShouldBe(lemonade.Revenue.ToDouble(), tolerance: 1e-9);
        _engine.AngelBonus.ToDouble().ShouldBe(1.0);
    }

    [Fact]
    public async Task Tick_AngelsAlsoBoostLifetimeEarnings()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));
        SetAngels(new BigDouble(50));

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0;

        var ltBefore = _engine.LifetimeEarnings;
        _engine.Tick(0.0);

        (_engine.LifetimeEarnings - ltBefore).ToDouble()
            .ShouldBe(lemonade.Revenue.ToDouble() * FiftyAngelBonus, tolerance: 1e-9);
    }

    [Fact]
    public async Task OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice()
    {
        // Invariant: offline earnings for N cycles == live earnings for N cycles
        // when angels are present.
        var pastTime = DateTime.UtcNow.AddSeconds(-60);
        var savedState = new GameState
        {
            CashText = "0",
            LifetimeEarningsText = "0",
            AngelInvestorsText = "50",
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

        var expected = 100.0 * FiftyAngelBonus; // ≈ 269.16
        var actualCash = engine.Cash.ToDouble();
        actualCash.ShouldBeInRange(expected - 15, expected + 15);
        engine.AngelBonus.ToDouble().ShouldBe(FiftyAngelBonus, tolerance: 1e-9);
    }

    // ---------------- ApplyOfflineEarnings public API ----------------

    [Fact]
    public async Task ApplyOfflineEarnings_ShouldAddToCashAndLifetime()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var cashBefore = _engine.Cash;
        var ltBefore = _engine.LifetimeEarnings;

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        earned.Sign.ShouldBeGreaterThan(0);
        (_engine.Cash - cashBefore).ToDouble().ShouldBe(earned.ToDouble(), tolerance: 1e-9);
        (_engine.LifetimeEarnings - ltBefore).ToDouble().ShouldBe(earned.ToDouble(), tolerance: 1e-9);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NoManagedBusinesses_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));
        _engine.BuyBusiness("lemonade"); // owned but no manager

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromMinutes(10));
        earned.IsZero.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NoBusinessesOwned_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromMinutes(10));
        earned.IsZero.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyOfflineEarnings_TinyGap_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        _engine.ApplyOfflineEarnings(TimeSpan.FromMilliseconds(500)).IsZero.ShouldBeTrue();
        _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(1)).IsZero.ShouldBeTrue();
        _engine.ApplyOfflineEarnings(TimeSpan.Zero).IsZero.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NegativeGap_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var cashBefore = _engine.Cash;
        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(-30));

        earned.IsZero.ShouldBeTrue();
        _engine.Cash.ShouldBe(cashBefore);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_AppliesAngelBonus()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));
        SetAngels(new BigDouble(50));
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        // Drain the auto-start cycle so we measure offline only.
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.ProgressPercent = 0;

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));
        earned.ToDouble().ShouldBe(100.0 * FiftyAngelBonus, tolerance: 1e-7);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_AndLiveTick_AreEquivalent()
    {
        // Strong invariant: applying offline earnings for N seconds yields
        // the same amount as ticking the engine for N seconds.
        var repoOffline = Substitute.For<IGameStateRepository>();
        repoOffline.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var offlineEngine = new GameEngine(repoOffline, NullLogger<GameEngine>.Instance);
        await offlineEngine.LoadAsync();
        SetCashOn(offlineEngine, new BigDouble(1_000_000));
        offlineEngine.BuyBusiness("lemonade");
        offlineEngine.BuyManager("lemonade");
        offlineEngine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        var repoLive = Substitute.For<IGameStateRepository>();
        repoLive.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var liveEngine = new GameEngine(repoLive, NullLogger<GameEngine>.Instance);
        await liveEngine.LoadAsync();
        SetCashOn(liveEngine, new BigDouble(1_000_000));
        liveEngine.BuyBusiness("lemonade");
        liveEngine.BuyManager("lemonade");
        liveEngine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        var cashBeforeOffline = offlineEngine.Cash;
        var cashBeforeLive = liveEngine.Cash;

        offlineEngine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        for (var i = 0; i < 600; i++) liveEngine.Tick(0.1);

        var earnedOffline = (offlineEngine.Cash - cashBeforeOffline).ToDouble();
        var earnedLive = (liveEngine.Cash - cashBeforeLive).ToDouble();

        // Live tick uses integer-cycle counting which can leave a small residual.
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
        GameEngine.CalculateAngels(new BigDouble(1e11)).IsZero.ShouldBeTrue();

    [Fact]
    public void CalculateAngels_ShouldReturnPositiveAboveThreshold() =>
        GameEngine.CalculateAngels(new BigDouble(1e14)).Sign.ShouldBeGreaterThan(0);

    [Fact]
    public async Task ExportToString_ShouldReturnBase64()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(42.5));

        var exported = _engine.ExportToString();

        exported.ShouldNotBeNullOrWhiteSpace();
        var bytes = Convert.FromBase64String(exported);
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        json.ShouldContain("\"cash\"");
        // BigDouble's canonical form for 42.5 is "4.25e1".
        json.ShouldContain("4.25e1");
    }

    [Fact]
    public async Task ImportFromString_ShouldRestoreState()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(9999));

        for (var i = 0; i < 5; i++)
            _engine.BuyBusiness("lemonade");

        _engine.BuyManager("lemonade");

        var exported = _engine.ExportToString();

        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync();
        engine2.Cash.ToDouble().ShouldBe(5.0);

        var result = engine2.ImportFromString(exported);
        result.ShouldBeTrue();
        engine2.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(5);
        engine2.Businesses.First(b => b.Id == "lemonade").HasManager.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportImport_ShouldRoundTrip()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(12345.67));

        var exported = _engine.ExportToString();
        var result = _engine.ImportFromString(exported);

        result.ShouldBeTrue();
        _engine.Cash.ToDouble().ShouldBe(12345.67, tolerance: 1e-9);
    }

    [Fact]
    public void ImportFromString_InvalidBase64_ShouldReturnFalse() =>
        _engine.ImportFromString("not-valid-base64!!!").ShouldBeFalse();

    [Fact]
    public void ImportFromString_InvalidJson_ShouldReturnFalse()
    {
        var bad = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not json"));
        _engine.ImportFromString(bad).ShouldBeFalse();
    }

    [Fact]
    public void ImportFromString_EmptyString_ShouldReturnFalse() =>
        _engine.ImportFromString("").ShouldBeFalse();

    /// <summary>
    /// Legacy v1 export format (numbers as JSON numbers, not strings)
    /// must still import — old saves predate the BigDouble migration.
    /// </summary>
    [Fact]
    public async Task ImportFromString_LegacyV1Format_ShouldStillWork()
    {
        await _engine.LoadAsync();
        // Construct a v1 export manually (numbers as JSON numbers).
        var legacyJson = """
        {
            "v": 1,
            "cash": 12345.67,
            "lifetime": 1000000.0,
            "angels": 50.0,
            "prestige": 2,
            "businesses": {"lemonade": 10},
            "managers": {"lemonade": true}
        }
        """;
        var legacyEncoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        _engine.ImportFromString(legacyEncoded).ShouldBeTrue();
        _engine.Cash.ToDouble().ShouldBe(12345.67, tolerance: 1e-9);
        _engine.AngelInvestors.ToDouble().ShouldBe(50.0);
        _engine.PrestigeCount.ShouldBe(2);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(10);
    }

    [Fact]
    public async Task Prestige_ShouldGiveStartingCash()
    {
        await _engine.LoadAsync();
        SetLifetime(new BigDouble(1e14));

        var (angels, success) = _engine.Prestige();
        success.ShouldBeTrue();
        angels.Sign.ShouldBeGreaterThan(0);

        // After prestige, player must have $5 to buy the first lemonade stand.
        _engine.Cash.ToDouble().ShouldBe(5.0);
        _engine.Businesses.All(b => b.Owned == 0).ShouldBeTrue();
    }

    [Fact]
    public async Task Prestige_CashShouldCoverFirstLemonade()
    {
        await _engine.LoadAsync();
        SetLifetime(new BigDouble(1e14));

        var (_, success) = _engine.Prestige();
        success.ShouldBeTrue();

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.NextCost.ToDouble().ShouldBe(4.0);
        (_engine.Cash >= lemonade.NextCost).ShouldBeTrue();
        _engine.BuyBusiness("lemonade").ShouldBeTrue();
    }

    // ---------------- BuyMultiple / BuyMax ----------------

    [Fact]
    public async Task BuyMultiple_ShouldBuyRequestedCount()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));

        var bought = _engine.BuyMultiple("lemonade", 10);
        bought.ShouldBe(10);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(10);
    }

    [Fact]
    public async Task BuyMultiple_NotEnoughCash_ShouldBuyPartial()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(10));

        var bought = _engine.BuyMultiple("lemonade", 100);
        // $10 affords 2 lemonade ($4 + $4.28 = $8.28 cumulative).
        bought.ShouldBe(2);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(2);
    }

    [Fact]
    public async Task BuyMultiple_ZeroCount_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));
        _engine.BuyMultiple("lemonade", 0).ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_InvalidBusiness_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1000));
        _engine.BuyMultiple("nonexistent", 5).ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_WithManager_ShouldAutoStart()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1_000_000));

        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = false;

        _engine.BuyMultiple("lemonade", 5);
        lemonade.IsRunning.ShouldBeTrue();
    }

    /// <summary>
    /// BuyMax is the deep-game purchase action — it buys as many units as
    /// the player can afford. With BigDouble cash this can be a huge number,
    /// and the closed-form geometric-series solver keeps the call O(1).
    /// </summary>
    [Fact]
    public async Task BuyMax_HugeCash_BuysManyUnits()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 50));

        var bought = _engine.BuyMax("lemonade");
        bought.ShouldBeGreaterThan(100);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(bought);
    }

    [Fact]
    public async Task BuyMax_NoCash_ShouldReturnZero()
    {
        await _engine.LoadAsync();
        SetCash(BigDouble.Zero);
        _engine.BuyMax("lemonade").ShouldBe(0);
    }

    /// <summary>
    /// The closed-form geometric-series math must never overdraw the
    /// player's cash, even at integer-cycle boundaries where floating-point
    /// noise could otherwise nudge total-cost just over the cash limit.
    /// </summary>
    [Fact]
    public async Task BuyMultiple_GeometricSeries_NeverOverdraws()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(100_000));

        var cashBefore = _engine.Cash;
        var bought = _engine.BuyMultiple("lemonade", 10_000);
        bought.ShouldBeGreaterThan(0);

        // Cash should be ≥ 0 (never overdrew).
        _engine.Cash.Sign.ShouldBeGreaterThanOrEqualTo(0);
        // And cash spent should not exceed starting cash.
        (cashBefore - _engine.Cash).Sign.ShouldBeGreaterThanOrEqualTo(0);
    }

    // ---------------- AngelBonus ----------------

    [Fact]
    public void AngelBonus_AtZeroAngels_ShouldBeOne() =>
        _engine.AngelBonus.ToDouble().ShouldBe(1.0);

    [Fact]
    public void AngelBonus_Compounds_NotLinear()
    {
        SetAngels(new BigDouble(50));
        _engine.AngelBonus.ToDouble().ShouldBeGreaterThan(2.5);
        _engine.AngelBonus.ToDouble().ShouldBeLessThan(2.8);
    }

    [Fact]
    public void AngelBonus_NegativeAngelInvestors_ShouldBeOne()
    {
        // Defensive: corrupted save with negative angels must not produce
        // a sub-1.0 multiplier (1.02^-N < 1).
        SetAngels(new BigDouble(-100));
        _engine.AngelBonus.ToDouble().ShouldBe(1.0);
    }

    /// <summary>
    /// Defect-1 regression: AngelBonus must remain finite at any angel
    /// count. Under BigDouble the cap saturates the exponent rather than
    /// the value itself, so the bonus stays astronomical but never
    /// becomes BigDouble.PositiveInfinity (which would propagate into cash).
    /// </summary>
    [Fact]
    public void AngelBonus_PastOverflowPoint_StaysFinite()
    {
        SetAngels(new BigDouble(50_000));
        var bonus = _engine.AngelBonus;
        bonus.IsFinite.ShouldBeTrue();
        // 1.02^50000 ≈ 10^430 — still a real value under BigDouble.
        bonus.Exponent.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void AngelBonus_AtAbsurdAngels_StillFiniteAndCapped()
    {
        // 10^100 angels — far past anything achievable. Bonus is saturated
        // at MaxAngelBonusExponent but remains a finite BigDouble.
        SetAngels(new BigDouble(1.0, 100));
        _engine.AngelBonus.IsFinite.ShouldBeTrue();
    }

    // ---------------- BigDouble-specific: 10^200 unblock ----------------

    /// <summary>
    /// The user's exact symptom: stuck at cash = 1e200, lifetime = 1e200.
    /// With BigDouble these clamps are gone — cash continues to grow.
    /// <para>
    /// We assert the unblock at two levels:
    /// </para>
    /// <list type="number">
    ///   <item>The engine's <c>SanitizeMoney</c> path doesn't clamp 1e200 back
    ///         to a finite ceiling (it preserves the value). This is the
    ///         direct counterpart to the deleted <c>MaxMoney = 1e200</c>
    ///         constant in the old engine.</item>
    ///   <item>BigDouble arithmetic on values past the old ceiling produces
    ///         the correct sum when the added revenue is large enough to
    ///         clear the 17-digit precision gap — that is, the only reason
    ///         a tiny per-tick revenue gets "absorbed" by 1e200 cash is
    ///         floating-point precision, not an engine-level clamp.</item>
    /// </list>
    /// <para>
    /// We deliberately avoid the previous formulation ("tick 10 times,
    /// assert exponent > 200") because in-game per-tick revenue at 1000
    /// owned is on the order of 10^14 — 186 orders of magnitude below
    /// 1e200 — so the precision gap absorbs it. That absorption is a
    /// BigDouble-correctness fact, not the bug we're guarding against.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Cash_AtFormerCap_IsNotClamped()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 200));
        SetLifetime(new BigDouble(1.0, 200));

        // 1: SanitizeMoney must preserve magnitude. Persist + reload via
        // the save round-trip exercises the same SanitizeMoney path the
        // engine uses on every state mutation.
        await _engine.SaveAsync();

        // Reload from the saved state (the substitute repo we wired returns
        // null for GetLatestAsync, so capture the saved arg via NSubstitute).
        var savedCalls = _repo.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IGameStateRepository.SaveAsync))
            .ToList();
        savedCalls.Count.ShouldBeGreaterThan(0);
        var savedState = (GameState)savedCalls[^1].GetArguments()[0]!;

        // The persisted cash text must round-trip back to (1.0, 200) —
        // proving the engine never clamped it on save.
        var roundTripped = BigDouble.Parse(savedState.CashText);
        roundTripped.Exponent.ShouldBe(200);
        roundTripped.Mantissa.ShouldBe(1.0, tolerance: 1e-12);

        // The live engine state likewise must still be 1e200, not clamped.
        _engine.Cash.Exponent.ShouldBe(200);
        _engine.Cash.IsFinite.ShouldBeTrue();
        _engine.LifetimeEarnings.Exponent.ShouldBe(200);

        // 2: Adding revenue of a comparable magnitude pushes cash past 1e200.
        // This proves the "stuck at 1e200" symptom is gone — when revenue
        // does reach magnitudes that BigDouble's 17-digit precision can
        // express against the cash exponent, cash genuinely grows.
        // The old engine would have clamped this back to 1e200; the new
        // one lets it grow freely.
        var simulatedRevenue = new BigDouble(5.0, 200);
        SetCash(_engine.Cash + simulatedRevenue);

        _engine.Cash.Exponent.ShouldBe(200);
        _engine.Cash.Mantissa.ShouldBe(6.0, tolerance: 1e-12);

        // And again — keep pushing, exponent climbs.
        SetCash(_engine.Cash + new BigDouble(1.0, 201));
        _engine.Cash.Exponent.ShouldBe(201);
        _engine.Cash.IsFinite.ShouldBeTrue();
    }

    [Fact]
    public async Task Tick_AtMaxAngels_CashStaysFinite()
    {
        await _engine.LoadAsync();
        SetAngels(new BigDouble(1.0, 6)); // 1 million angels

        foreach (var biz in _engine.Businesses)
        {
            biz.Owned = 1000;
            biz.HasManager = true;
            biz.IsRunning = true;
            biz.ProgressPercent = 100.0;
        }

        for (var i = 0; i < 100; i++)
        {
            foreach (var biz in _engine.Businesses) biz.ProgressPercent = 100.0;
            _engine.Tick(0.0);
        }

        _engine.Cash.IsFinite.ShouldBeTrue();
        _engine.LifetimeEarnings.IsFinite.ShouldBeTrue();
    }

    // ---------------- LoadAsync sanity / migration ----------------

    [Fact]
    public async Task LoadAsync_WithInfinityInSave_ShouldClampToZero()
    {
        // Defensive: a corrupted save with Infinity in cash must produce
        // a playable game. SanitizeMoney maps Infinity → 0.
        var pastTime = DateTime.UtcNow.AddSeconds(-30);
        var savedState = new GameState
        {
            CashText = "Infinity",
            LifetimeEarningsText = "Infinity",
            AngelInvestorsText = "60000",
            BusinessDataJson = """{"lemonade":1000}""",
            ManagerDataJson = """{"lemonade":true}""",
            LastPlayedAt = pastTime,
            UpdatedAt = pastTime
        };

        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(savedState));

        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync();

        engine.Cash.IsFinite.ShouldBeTrue();
        engine.LifetimeEarnings.IsFinite.ShouldBeTrue();
        engine.AngelInvestors.IsFinite.ShouldBeTrue();
        engine.AngelBonus.IsFinite.ShouldBeTrue();
    }

    /// <summary>
    /// The user's actual save uses values like "1e200" — make sure those
    /// round-trip correctly through Parse → engine state.
    /// <para>
    /// The gap from <c>LastPlayedAt</c> to "now" must stay strictly below
    /// <c>GameEngine.MinimumOfflineGapSeconds</c> (1.0s). If it crosses that
    /// threshold, <c>LoadAsync</c> calls <see cref="GameEngine.ApplyOfflineEarnings"/>,
    /// which with the test's 1e9 angels compounds to an angel bonus of
    /// roughly 10^8,600,000 — utterly dominating the 1e200 starting cash
    /// and corrupting the assertions about pure-load behavior.
    /// </para>
    /// <para>
    /// 100ms is safely sub-threshold across any plausible test-runner
    /// timing jitter (CI cold-start, GC pauses) while still being a
    /// realistic LastPlayedAt value.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithBigDoubleStringInSave_LoadsExactly()
    {
        var pastTime = DateTime.UtcNow.AddMilliseconds(-100); // sub-threshold
        var savedState = new GameState
        {
            CashText = "1e200",
            LifetimeEarningsText = "1e200",
            AngelInvestorsText = "1e9",
            BusinessDataJson = """{"lemonade":1100,"newspaper":1000,"carwash":1000,"pizza":1000,"donut":1000,"shrimp":2270}""",
            ManagerDataJson = """{"lemonade":true,"newspaper":true,"carwash":true,"pizza":true,"donut":true,"shrimp":true}""",
            LastPlayedAt = pastTime,
            UpdatedAt = pastTime
        };

        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(savedState));

        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync();

        engine.Cash.Exponent.ShouldBe(200);
        engine.Cash.Mantissa.ShouldBe(1.0, tolerance: 1e-12);
        engine.LifetimeEarnings.Exponent.ShouldBe(200);
        engine.AngelInvestors.Exponent.ShouldBe(9);
        engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1100);
    }

    [Fact]
    public async Task ExportToString_PreservesExtremeMagnitudes()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(7.5, 500)); // far past the old 1e200 ceiling
        SetLifetime(new BigDouble(3.2, 800));

        var exported = _engine.ExportToString();
        exported.ShouldNotBeNullOrWhiteSpace();

        // Round-trip the export string and check magnitudes.
        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync();
        engine2.ImportFromString(exported).ShouldBeTrue();

        engine2.Cash.Exponent.ShouldBe(500);
        engine2.Cash.Mantissa.ShouldBe(7.5, tolerance: 1e-12);
        engine2.LifetimeEarnings.Exponent.ShouldBe(800);
    }

    // ---------------- PostMilestoneScaling ----------------

    [Fact]
    public void PostMilestoneScaling_BelowCap_IsExactlyOne()
    {
        var biz = new Business
        {
            Id = "t",
            Name = "T",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 999
        };
        biz.PostMilestoneScaling.ToDouble().ShouldBe(1.0);
    }

    [Fact]
    public void PostMilestoneScaling_AtCap_IsExactlyOne()
    {
        var biz = new Business
        {
            Id = "t",
            Name = "T",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 1000
        };
        biz.PostMilestoneScaling.ToDouble().ShouldBe(1.0);
    }

    [Fact]
    public void PostMilestoneScaling_PastCap_GrowsAsSqrtOfCost()
    {
        var biz = new Business
        {
            Id = "t",
            Name = "T",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 1100
        };
        // 1.07^50 ≈ 29.46
        biz.PostMilestoneScaling.ToDouble().ShouldBe(Math.Pow(1.07, 50), tolerance: 1e-9);
    }

    [Fact]
    public void Revenue_BelowCap_DoesNotIncludePostMilestoneScaling()
    {
        var biz = new Business
        {
            Id = "t",
            Name = "T",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 5,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 100
        };
        biz.Revenue.ToDouble().ShouldBe(5 * 100 * 8, tolerance: 1e-9);
    }

    [Fact]
    public void Revenue_PastCap_IncludesPostMilestoneScaling()
    {
        var biz = new Business
        {
            Id = "t",
            Name = "T",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
            Owned = 1100
        };
        var expected = 1.0 * 1100 * biz.MilestoneMultiplier * Math.Pow(1.07, 50);
        biz.Revenue.ToDouble().ShouldBe(expected, tolerance: 1e-3);
    }

    [Fact]
    public void Business_AffordableCount_NonFiniteCash_StaysSafe()
    {
        var biz = new Business
        {
            Id = "t",
            Name = "T",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.07,
        };
        biz.AffordableCount(BigDouble.PositiveInfinity).ShouldBeGreaterThanOrEqualTo(0);
        biz.AffordableCount(BigDouble.NaN).ShouldBe(0);
    }

    // ---------------- Test helpers ----------------

    private void SetCash(BigDouble amount) => SetCashOn(_engine, amount);

    private static void SetCashOn(GameEngine engine, BigDouble amount)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        prop.GetSetMethod(true)!.Invoke(engine, [amount]);
    }

    private void SetAngels(BigDouble count)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.AngelInvestors))!;
        prop.GetSetMethod(true)!.Invoke(_engine, [count]);
    }

    private void SetLifetime(BigDouble amount)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.LifetimeEarnings))!;
        prop.GetSetMethod(true)!.Invoke(_engine, [amount]);
    }
}
