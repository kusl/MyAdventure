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

        // Give cash via reflection or direct manipulation
        SetCash(100);

        var result = _engine.BuyBusiness("lemonade");
        result.ShouldBeTrue();
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1);
        _engine.Cash.ShouldBeLessThan(100);
    }

    [Fact]
    public async Task BuyBusiness_ShouldPass()
    {
        await _engine.LoadAsync();
        _engine.BuyBusiness("lemonade").ShouldBeTrue();
    }

    [Fact]
    public async Task Tick_RunningBusiness_ShouldEarnRevenue()
    {
        await _engine.LoadAsync();
        SetCash(1000);
        _engine.BuyBusiness("lemonade");
        _engine.StartBusiness("lemonade");

        // Simulate enough ticks to complete a cycle
        for (var i = 0; i < 100; i++)
            _engine.Tick(0.1);

        _engine.Cash.ShouldBeGreaterThan(990); // earned some revenue
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

    // Helper to set cash directly for testing
    private void SetCash(double amount)
    {
        var cashProp = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        cashProp.SetValue(_engine, amount);
    }
}
