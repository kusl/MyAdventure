using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class GameEngineTests
{
    private static readonly double FiftyAngelBonus = 1.0 + GameBalance.AngelBonusPerAngel * 50;

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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        _engine.Cash.ToDouble().ShouldBe(5.0);
        _engine.Businesses.Count.ShouldBe(6);
    }

    [Fact]
    public async Task BuyBusiness_ShouldDeductCashAndIncrementOwned()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(100));

        var result = _engine.BuyBusiness("lemonade");
        result.ShouldBeTrue();
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1);
        _engine.Cash.ShouldBeLessThan(new BigDouble(100));
    }

    [Fact]
    public async Task BuyBusiness_NotEnoughCash_ShouldFail()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        _engine.BuyBusiness("newspaper").ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_RunningBusiness_ShouldEarnRevenue()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1_000_000));

        for (var i = 0; i < 25; i++)
            _engine.BuyBusiness("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.Owned.ShouldBe(25);
        lemonade.MilestoneMultiplier.ShouldBe(2.0);
        lemonade.Revenue.ToDouble().ShouldBe(lemonade.BaseRevenue * 25 * 2.0 * lemonade.Efficiency.ToDouble(), tolerance: 1e-9);
    }

    [Fact]
    public async Task Tick_WithAngels_ShouldApplyAngelBonusToLiveEarnings()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1000));
        SetAngels(new BigDouble(50));

        _engine.BuyBusiness("lemonade");
        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 100.0;

        var cashBefore = _engine.Cash;
        _engine.Tick(0.0);

        var earned = (_engine.Cash - cashBefore).ToDouble();
        earned.ShouldBe(lemonade.Revenue.ToDouble() * FiftyAngelBonus, tolerance: 1e-9);
    }

    [Fact]
    public async Task Tick_NoAngels_ShouldEarnExactlyBaseRevenue()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await engine.LoadAsync(TestContext.Current.CancellationToken);

        var expected = 100.0 * engine.Businesses.First(b => b.Id == "lemonade").Revenue.ToDouble() * FiftyAngelBonus;
        var actualCash = engine.Cash.ToDouble();
        actualCash.ShouldBeInRange(expected - 15, expected + 15);
        engine.AngelBonus.ToDouble().ShouldBe(FiftyAngelBonus, tolerance: 1e-9);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_ShouldAddToCashAndLifetime()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1000));
        _engine.BuyBusiness("lemonade");

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromMinutes(10));
        earned.IsZero.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyOfflineEarnings_NoBusinessesOwned_ShouldReturnZero()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromMinutes(10));
        earned.IsZero.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyOfflineEarnings_TinyGap_ShouldReturnZero()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1_000_000));
        SetAngels(new BigDouble(50));
        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.ProgressPercent = 0;

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));
        earned.ToDouble().ShouldBe(100.0 * lemonade.Revenue.ToDouble() * FiftyAngelBonus, tolerance: 1e-7);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_AndLiveTick_AreEquivalent()
    {
        var repoOffline = Substitute.For<IGameStateRepository>();
        repoOffline.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var offlineEngine = new GameEngine(repoOffline, NullLogger<GameEngine>.Instance);
        await offlineEngine.LoadAsync(TestContext.Current.CancellationToken);
        SetCashOn(offlineEngine, new BigDouble(1_000_000));
        offlineEngine.BuyBusiness("lemonade");
        offlineEngine.BuyManager("lemonade");
        offlineEngine.Businesses.First(b => b.Id == "lemonade").ProgressPercent = 0;

        var repoLive = Substitute.For<IGameStateRepository>();
        repoLive.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var liveEngine = new GameEngine(repoLive, NullLogger<GameEngine>.Instance);
        await liveEngine.LoadAsync(TestContext.Current.CancellationToken);
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

        Math.Abs(earnedOffline - earnedLive).ShouldBeLessThan(1.5);
    }

    [Fact]
    public async Task Prestige_NotEnoughEarnings_ShouldFail()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(42.5));

        var exported = _engine.ExportToString();

        exported.ShouldNotBeNullOrWhiteSpace();
        var bytes = Convert.FromBase64String(exported);
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        json.ShouldContain("\"cash\"");
        json.ShouldContain("4.25e1");
    }

    [Fact]
    public async Task ImportFromString_ShouldRestoreState()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(9999));

        for (var i = 0; i < 5; i++)
            _engine.BuyBusiness("lemonade");

        _engine.BuyManager("lemonade");

        var exported = _engine.ExportToString();

        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync(TestContext.Current.CancellationToken);
        engine2.Cash.ToDouble().ShouldBe(5.0);

        var result = engine2.ImportFromString(exported);
        result.ShouldBeTrue();
        engine2.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(5);
        engine2.Businesses.First(b => b.Id == "lemonade").HasManager.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportImport_ShouldRoundTrip()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task ImportFromString_LegacyV1Format_ShouldStillWork()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
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
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetLifetime(new BigDouble(1e14));

        var (angels, success) = _engine.Prestige();
        success.ShouldBeTrue();
        angels.Sign.ShouldBeGreaterThan(0);

        _engine.Cash.ToDouble().ShouldBe(5.0);
        _engine.Businesses.All(b => b.Owned == 0).ShouldBeTrue();
    }

    [Fact]
    public async Task Prestige_CashShouldCoverFirstLemonade()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetLifetime(new BigDouble(1e14));

        var (_, success) = _engine.Prestige();
        success.ShouldBeTrue();

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.NextCost.ToDouble().ShouldBe(4.0);
        (_engine.Cash >= lemonade.NextCost).ShouldBeTrue();
        _engine.BuyBusiness("lemonade").ShouldBeTrue();
    }

    [Fact]
    public async Task BuyMultiple_ShouldBuyRequestedCount()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1_000_000));

        var bought = _engine.BuyMultiple("lemonade", 10);
        bought.ShouldBe(10);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(10);
    }

    [Fact]
    public async Task BuyMultiple_NotEnoughCash_ShouldBuyPartial()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(10));

        var bought = _engine.BuyMultiple("lemonade", 100);
        bought.ShouldBe(2);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(2);
    }

    [Fact]
    public async Task BuyMultiple_ZeroCount_ShouldReturnZero()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1000));
        _engine.BuyMultiple("lemonade", 0).ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_InvalidBusiness_ShouldReturnZero()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1000));
        _engine.BuyMultiple("nonexistent", 5).ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_WithManager_ShouldAutoStart()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1_000_000));

        _engine.BuyBusiness("lemonade");
        _engine.BuyManager("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.IsRunning = false;

        _engine.BuyMultiple("lemonade", 5);
        lemonade.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public async Task BuyMax_HugeCash_BuysManyUnits()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 50));

        var bought = _engine.BuyMax("lemonade");
        bought.ShouldBeGreaterThan(100);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(bought);
    }

    [Fact]
    public async Task BuyMax_NoCash_ShouldReturnZero()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(BigDouble.Zero);
        _engine.BuyMax("lemonade").ShouldBe(0);
    }

    [Fact]
    public async Task BuyMultiple_GeometricSeries_NeverOverdraws()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(100_000));

        var cashBefore = _engine.Cash;
        var bought = _engine.BuyMultiple("lemonade", 10_000);
        bought.ShouldBeGreaterThan(0);

        _engine.Cash.Sign.ShouldBeGreaterThanOrEqualTo(0);
        (cashBefore - _engine.Cash).Sign.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void AngelBonus_AtZeroAngels_ShouldBeOne() =>
        _engine.AngelBonus.ToDouble().ShouldBe(1.0);

    [Fact]
    public void AngelBonus_IsLinearInAngels()
    {
        SetAngels(new BigDouble(50));
        _engine.AngelBonus.ToDouble().ShouldBe(2.0, tolerance: 1e-12);
        SetAngels(new BigDouble(100));
        _engine.AngelBonus.ToDouble().ShouldBe(3.0, tolerance: 1e-12);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1e6)]
    [InlineData(1e200)]
    public void AngelBonus_DoublingAngels_NeverMoreThanDoublesTheBonus(double angels)
    {
        var bonus = GameBalance.AngelBonusFor(new BigDouble(angels));
        var doubled = GameBalance.AngelBonusFor(new BigDouble(angels * 2));
        (doubled / bonus).ToDouble().ShouldBeLessThanOrEqualTo(2.0);
    }

    [Fact]
    public void AngelBonus_NegativeAngelInvestors_ShouldBeOne()
    {
        SetAngels(new BigDouble(-100));
        _engine.AngelBonus.ToDouble().ShouldBe(1.0);
    }

    [Fact]
    public void AngelBonus_PastOverflowPoint_StaysFinite()
    {
        SetAngels(new BigDouble(50_000));
        var bonus = _engine.AngelBonus;
        bonus.IsFinite.ShouldBeTrue();
        bonus.ToDouble().ShouldBe(1001.0, tolerance: 1e-9);
    }

    [Fact]
    public void AngelBonus_AtAbsurdAngels_StillFiniteAndCapped()
    {
        SetAngels(new BigDouble(1.0, 100));
        _engine.AngelBonus.IsFinite.ShouldBeTrue();
        _engine.AngelBonus.Exponent.ShouldBe(98);
    }

    [Fact]
    public async Task Cash_AtFormerCap_IsNotClamped()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 200));
        SetLifetime(new BigDouble(1.0, 200));

        await _engine.SaveAsync(TestContext.Current.CancellationToken);

        var savedCalls = _repo.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IGameStateRepository.SaveAsync))
            .ToList();
        savedCalls.Count.ShouldBeGreaterThan(0);
        var savedState = (GameState)savedCalls[^1].GetArguments()[0]!;

        var roundTripped = BigDouble.Parse(savedState.CashText);
        roundTripped.Exponent.ShouldBe(200);
        roundTripped.Mantissa.ShouldBe(1.0, tolerance: 1e-12);

        _engine.Cash.Exponent.ShouldBe(200);
        _engine.Cash.IsFinite.ShouldBeTrue();
        _engine.LifetimeEarnings.Exponent.ShouldBe(200);

        var simulatedRevenue = new BigDouble(5.0, 200);
        SetCash(_engine.Cash + simulatedRevenue);

        _engine.Cash.Exponent.ShouldBe(200);
        _engine.Cash.Mantissa.ShouldBe(6.0, tolerance: 1e-12);

        SetCash(_engine.Cash + new BigDouble(1.0, 201));
        _engine.Cash.Exponent.ShouldBe(201);
        _engine.Cash.IsFinite.ShouldBeTrue();
    }

    [Fact]
    public async Task Tick_AtMaxAngels_CashStaysFinite()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetAngels(new BigDouble(1.0, 6));

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

    [Fact]
    public async Task LoadAsync_WithInfinityInSave_ShouldClampToZero()
    {
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
        await engine.LoadAsync(TestContext.Current.CancellationToken);

        engine.Cash.IsFinite.ShouldBeTrue();
        engine.LifetimeEarnings.IsFinite.ShouldBeTrue();
        engine.AngelInvestors.IsFinite.ShouldBeTrue();
        engine.AngelBonus.IsFinite.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_WithBigDoubleStringInSave_LoadsExactly()
    {
        var pastTime = DateTime.UtcNow.AddMilliseconds(-100);
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
        await engine.LoadAsync(TestContext.Current.CancellationToken);

        engine.Cash.Exponent.ShouldBe(200);
        engine.Cash.Mantissa.ShouldBe(1.0, tolerance: 1e-12);
        engine.LifetimeEarnings.Exponent.ShouldBe(200);
        engine.AngelInvestors.Exponent.ShouldBe(9);
        engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1100);
    }

    [Fact]
    public async Task ExportToString_PreservesExtremeMagnitudes()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(7.5, 500));
        SetLifetime(new BigDouble(3.2, 800));

        var exported = _engine.ExportToString();
        exported.ShouldNotBeNullOrWhiteSpace();

        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync(TestContext.Current.CancellationToken);
        engine2.ImportFromString(exported).ShouldBeTrue();

        engine2.Cash.Exponent.ShouldBe(500);
        engine2.Cash.Mantissa.ShouldBe(7.5, tolerance: 1e-12);
        engine2.LifetimeEarnings.Exponent.ShouldBe(800);
    }

    [Theory]
    [InlineData(1.07)]
    [InlineData(1.11)]
    [InlineData(1.15)]
    public void Efficiency_ZeroOwned_IsExactlyOne(double r) =>
        GameBalance.Efficiency(0, r).ShouldBe(BigDouble.One);

    [Theory]
    [InlineData(0.9)]
    [InlineData(1.0)]
    [InlineData(1.01)]
    [InlineData(1.07)]
    [InlineData(1.15)]
    public void Efficiency_IsNeverBelowOne(double r)
    {
        foreach (var owned in new long[] { 1, 10, 25, 100, 400, 999, 1000, 5000, 1_000_000 })
            GameBalance.Efficiency(owned, r).ShouldBeGreaterThanOrEqualTo(BigDouble.One);
    }

    [Fact]
    public void Efficiency_ForDefaultBusinesses_NeverDecreasesWithOwnership()
    {
        foreach (var biz in BusinessDefinitions.CreateDefaults())
        {
            var previous = 0.0;
            for (var owned = 0L; owned <= 3000L; owned++)
            {
                var log = GameBalance.EfficiencyLog10(owned, biz.CostMultiplier);
                log.ShouldBeGreaterThanOrEqualTo(previous - 1e-12);
                previous = log;
            }
        }
    }

    [Theory]
    [InlineData(1000L)]
    [InlineData(1100L)]
    [InlineData(5000L)]
    [InlineData(100_000L)]
    public void TotalMultiplier_FromNormalizationOnward_IsExactlyCostToTheEfficiencyExponent(long owned)
    {
        foreach (var biz in BusinessDefinitions.CreateDefaults())
        {
            biz.Owned = owned;
            var totalLog = Math.Log10(biz.MilestoneMultiplier) + Math.Log10(biz.SpeedMultiplier) + biz.Efficiency.Log10();
            var expectedLog = GameBalance.EfficiencyExponent * owned * Math.Log10(biz.CostMultiplier);
            totalLog.ShouldBe(expectedLog, tolerance: 1e-9 * Math.Max(1.0, expectedLog));
        }
    }

    [Fact]
    public void TotalMultiplier_BelowNormalization_StaysWithinBoundedWiggleOfTheCurve()
    {
        foreach (var biz in BusinessDefinitions.CreateDefaults())
        {
            for (var owned = 1L; owned < GameBalance.EfficiencyNormalizationOwned; owned++)
            {
                biz.Owned = owned;
                var totalLog = Math.Log10(biz.MilestoneMultiplier) + Math.Log10(biz.SpeedMultiplier) + biz.Efficiency.Log10();
                var curveLog = GameBalance.EfficiencyExponent * owned * Math.Log10(biz.CostMultiplier);
                Math.Abs(totalLog - curveLog).ShouldBeLessThan(1.25);
            }
        }
    }

    [Fact]
    public void Revenue_IsBaseTimesOwnedTimesMilestonesTimesEfficiency()
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
        biz.Revenue.ToDouble().ShouldBe(5 * 100 * 8 * biz.Efficiency.ToDouble(), tolerance: 1e-6);
    }

    [Theory]
    [InlineData(0.40)]
    [InlineData(0.50)]
    public void EfficiencyExponent_StaysInsideTheStableBand(double bound)
    {
        if (bound < 0.45) GameBalance.EfficiencyExponent.ShouldBeGreaterThan(bound);
        else GameBalance.EfficiencyExponent.ShouldBeLessThan(bound);
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

    [Fact]
    public async Task CrossBusinessSpeedMultiplier_NoBusinessesOwned_ReturnsOne()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        _engine.MinOwnedAcrossBusinesses.ShouldBe(0);
        _engine.CrossBusinessSpeedMultiplier.ShouldBe(BigDouble.One);
    }

    [Fact]
    public async Task CrossBusinessSpeedMultiplier_OneBusinessAtZero_ReturnsOne()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        foreach (var biz in _engine.Businesses)
            biz.Owned = 1000;
        _engine.Businesses.First(b => b.Id == "shrimp").Owned = 0;

        _engine.MinOwnedAcrossBusinesses.ShouldBe(0);
        _engine.CrossBusinessSpeedMultiplier.ShouldBe(BigDouble.One);
    }

    [Fact]
    public async Task CrossBusinessSpeedMultiplier_AllBizAt25_ReturnsOneStack()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        foreach (var biz in _engine.Businesses)
            biz.Owned = 25;

        _engine.MinOwnedAcrossBusinesses.ShouldBe(25);
        _engine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(GameBalance.CrossBusinessMultiplierPerThreshold, tolerance: 1e-9);
    }

    [Fact]
    public async Task CrossBusinessSpeedMultiplier_AllBizAt400_ReturnsMax()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        foreach (var biz in _engine.Businesses)
            biz.Owned = 400;

        _engine.MinOwnedAcrossBusinesses.ShouldBe(400);
        _engine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(CrossBusinessSpeedBonus.MaxMultiplier, tolerance: 1e-9);
    }

    [Fact]
    public async Task CrossBusinessSpeedMultiplier_AllBizAt1000_StaysAtMax()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        foreach (var biz in _engine.Businesses)
            biz.Owned = 1000;

        _engine.MinOwnedAcrossBusinesses.ShouldBe(1000);
        _engine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(CrossBusinessSpeedBonus.MaxMultiplier, tolerance: 1e-9);
    }

    [Fact]
    public async Task MinOwnedAcrossBusinesses_TracksLowestBusiness()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var businesses = _engine.Businesses.ToList();
        businesses[0].Owned = 100;
        businesses[1].Owned = 17;
        businesses[2].Owned = 200;
        businesses[3].Owned = 50;
        businesses[4].Owned = 75;
        businesses[5].Owned = 1000;

        _engine.MinOwnedAcrossBusinesses.ShouldBe(17);
    }

    [Fact]
    public async Task OfflineEarnings_ShouldApplyCrossBonusOnce_NotTwice()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(BigDouble.Zero);

        foreach (var biz in _engine.Businesses)
            biz.Owned = 25;

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.HasManager = true;
        lemonade.IsRunning = true;
        lemonade.ProgressPercent = 0;
        foreach (var biz in _engine.Businesses.Where(b => b.Id != "lemonade"))
        {
            biz.HasManager = false;
            biz.IsRunning = false;
        }

        var cross = GameBalance.CrossBusinessMultiplierPerThreshold;
        _engine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(cross, tolerance: 1e-9);

        var revenuePerSec = lemonade.RevenuePerSecond.ToDouble();
        var expectedSeconds = 60.0;
        var expected = revenuePerSec * expectedSeconds * cross;

        var earned = _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        var actual = earned.ToDouble();
        actual.ShouldBe(expected, tolerance: expected * 0.01);
    }

    [Fact]
    public async Task ApplyOfflineEarnings_AndLiveTick_AreEquivalent_WithCrossBonus()
    {
        async Task<GameEngine> MakeAsync()
        {
            var repo = Substitute.For<IGameStateRepository>();
            repo.GetLatestAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<GameState?>(null));
            var eng = new GameEngine(repo, NullLogger<GameEngine>.Instance);
            await eng.LoadAsync(TestContext.Current.CancellationToken);
            SetCashOn(eng, new BigDouble(1_000_000));
            foreach (var biz in eng.Businesses)
                biz.Owned = 50;
            var lemonade = eng.Businesses.First(b => b.Id == "lemonade");
            lemonade.HasManager = true;
            lemonade.IsRunning = true;
            lemonade.ProgressPercent = 0;
            foreach (var biz in eng.Businesses.Where(b => b.Id != "lemonade"))
            {
                biz.HasManager = false;
                biz.IsRunning = false;
            }
            return eng;
        }

        var offlineEngine = await MakeAsync();
        var liveEngine = await MakeAsync();

        var twoStacks = Math.Pow(GameBalance.CrossBusinessMultiplierPerThreshold, 2);
        offlineEngine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(twoStacks, tolerance: 1e-9);
        liveEngine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(twoStacks, tolerance: 1e-9);

        var cashBeforeOffline = offlineEngine.Cash;
        var cashBeforeLive = liveEngine.Cash;

        offlineEngine.ApplyOfflineEarnings(TimeSpan.FromSeconds(60));

        for (var i = 0; i < 600; i++) liveEngine.Tick(0.1);

        var earnedOffline = (offlineEngine.Cash - cashBeforeOffline).ToDouble();
        var earnedLive = (liveEngine.Cash - cashBeforeLive).ToDouble();

        var relDiff = Math.Abs(earnedOffline - earnedLive) / Math.Max(1.0, earnedOffline);
        relDiff.ShouldBeLessThan(0.01);
    }

    [Fact]
    public async Task ExportToString_IncludesTimestamp()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var exported = _engine.ExportToString();
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(exported));

        json.ShouldContain("\"timestamp\"");

        var doc = System.Text.Json.JsonDocument.Parse(json);
        var stampStr = doc.RootElement.GetProperty("timestamp").GetString();
        stampStr.ShouldNotBeNullOrWhiteSpace();
        var parsed = DateTime.Parse(stampStr!,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        (DateTime.UtcNow - parsed.ToUniversalTime()).Duration().TotalMinutes
            .ShouldBeLessThan(1.0);
    }

    [Fact]
    public async Task ExportToString_TimestampAdvances_BetweenExports()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var first = _engine.ExportToString();
        await Task.Delay(50);
        var second = _engine.ExportToString();

        string TimestampOf(string b64)
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("timestamp").GetString()!;
        }

        var t1 = DateTime.Parse(TimestampOf(first),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var t2 = DateTime.Parse(TimestampOf(second),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

        (t2 >= t1).ShouldBeTrue();
    }

    [Fact]
    public async Task ImportFromString_IgnoresTimestamp()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var json = """
        {
            "v": 2,
            "cash": "1.23e3",
            "lifetime": "0e0",
            "angels": "0e0",
            "prestige": 0,
            "businesses": {"lemonade": 3},
            "managers": {"lemonade": false},
            "timestamp": "this-is-not-a-date-at-all"
        }
        """;
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        _engine.ImportFromString(encoded).ShouldBeTrue();
        _engine.Cash.ToDouble().ShouldBe(1230.0, tolerance: 1e-9);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(3);
    }

    [Fact]
    public async Task ImportFromString_NoTimestamp_StillImports()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var json = """
        {
            "v": 2,
            "cash": "5e2",
            "lifetime": "5e2",
            "angels": "0e0",
            "prestige": 0,
            "businesses": {"lemonade": 5},
            "managers": {"lemonade": true}
        }
        """;
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        _engine.ImportFromString(encoded).ShouldBeTrue();
        _engine.Cash.ToDouble().ShouldBe(500.0, tolerance: 1e-9);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(5);
        _engine.Businesses.First(b => b.Id == "lemonade").HasManager.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportThenExport_TimestampReflectsNewExportTime_NotImportedOne()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var first = _engine.ExportToString();
        await Task.Delay(50);

        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync(TestContext.Current.CancellationToken);
        engine2.ImportFromString(first).ShouldBeTrue();

        var second = engine2.ExportToString();

        string TimestampOf(string b64)
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("timestamp").GetString()!;
        }

        var t1 = DateTime.Parse(TimestampOf(first),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var t2 = DateTime.Parse(TimestampOf(second),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

        (t2 > t1).ShouldBeTrue();
    }

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
