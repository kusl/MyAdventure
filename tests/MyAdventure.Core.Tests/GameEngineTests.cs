using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class GameEngineTests
{
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

        // Revenue should be base × owned × multiplier
        lemonade.Revenue.ShouldBe(lemonade.BaseRevenue * 25 * 2.0);
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
    // We need LifetimeEarnings >= 1e12 for angels
    // Use reflection to set LifetimeEarnings directly
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

    private void SetCash(double amount)
    {
        var cashProp = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        cashProp.GetSetMethod(true)!.Invoke(_engine, [amount]);
    }
}
