using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class OverflowRegressionTests
{
    private readonly IGameStateRepository _repo = Substitute.For<IGameStateRepository>();
    private readonly GameEngine _engine;

    public OverflowRegressionTests()
    {
        _repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        _engine = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
    }

    [Fact]
    public async Task BuyMultiple_StartingAtThreeBillionOwned_DoesNotWrapToNegative()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 200_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");
        shrimp.Owned = 3_000_000_000L;

        var bought = _engine.BuyMultiple("shrimp", 1_000_000_000L);

        bought.ShouldBe(1_000_000_000L);
        shrimp.Owned.ShouldBe(4_000_000_000L);
        shrimp.Owned.ShouldBeGreaterThan(0L);
    }

    [Fact]
    public async Task BuyMax_AtHugeOwnedCount_DoesNotProduceNegativeOwned()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 200_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");
        shrimp.Owned = 3_000_000_000L;

        _engine.BuyMax("shrimp");

        shrimp.Owned.ShouldBeGreaterThan(0L);
        shrimp.Owned.ShouldBeGreaterThan(3_000_000_000L);
    }

    [Fact]
    public async Task BuyBusiness_AtIntMaxValueOwned_DoesNotWrap()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 150_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");
        shrimp.Owned = (long)int.MaxValue;

        _engine.BuyBusiness("shrimp").ShouldBeTrue();

        shrimp.Owned.ShouldBe((long)int.MaxValue + 1L);
        shrimp.Owned.ShouldBeGreaterThan(0L);
    }

    [Fact]
    public void AffordableCount_NeverExceedsPracticalBatchCap()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.0001
        };

        biz.AffordableCount(BigDouble.PositiveInfinity)
            .ShouldBe(Business.PracticalBatchCap);

        biz.AffordableCount(new BigDouble(1.0, 100_000))
            .ShouldBeLessThanOrEqualTo(Business.PracticalBatchCap);
    }

    [Fact]
    public void PracticalBatchCap_LeavesHeadroomForRepeatedMaxBuys()
    {
        var sum = Business.PracticalBatchCap + Business.PracticalBatchCap;
        sum.ShouldBeGreaterThan(0L);
        var fourCaps = Business.PracticalBatchCap * 4L;
        fourCaps.ShouldBeGreaterThan(0L);
    }

    [Fact]
    public async Task ImportFromString_ProductionCorruptSave_LoadsClampedToZero()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var corruptJson = """
        {"v":2,"cash":"1.0e10","lifetime":"1.0e10","angels":"100","prestige":5,
         "businesses":{"lemonade":1000,"newspaper":1000,"carwash":1000,
                       "pizza":1000,"donut":1000,"shrimp":-294966296},
         "managers":{"lemonade":true,"newspaper":true,"carwash":true,
                     "pizza":true,"donut":true,"shrimp":true},
         "timestamp":"2026-05-26T23:50:26Z"}
        """;
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(corruptJson));

        _engine.ImportFromString(encoded).ShouldBeTrue();

        _engine.Businesses.First(b => b.Id == "shrimp").Owned.ShouldBe(0L);
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1000L);
    }

    [Fact]
    public async Task ImportFromString_HandEditedSaveWithLongRangeValue_LoadsCorrectly()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var json = """
        {"v":2,"cash":"1.0e10","lifetime":"1.0e10","angels":"100","prestige":1,
         "businesses":{"lemonade":5000000000,"newspaper":0,"carwash":0,
                       "pizza":0,"donut":0,"shrimp":0},
         "managers":{},
         "timestamp":"2026-05-26T23:50:26Z"}
        """;
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        _engine.ImportFromString(encoded).ShouldBeTrue();

        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(5_000_000_000L);
    }

    [Fact]
    public async Task ExportThenImport_LongRangeOwned_RoundTripsExactly()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        var lemon = _engine.Businesses.First(b => b.Id == "lemonade");
        lemon.Owned = 7_000_000_000L;

        var exported = _engine.ExportToString();

        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync(TestContext.Current.CancellationToken);
        engine2.ImportFromString(exported).ShouldBeTrue();

        engine2.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(7_000_000_000L);
    }

    [Fact]
    public async Task BuyMultiple_RepeatedAtCap_OwnedStaysNonNegativeAndMonotonic()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 10_000_000));
        var biz = _engine.Businesses.First(b => b.Id == "lemonade");

        var initial = biz.Owned;
        var last = initial;
        for (var i = 0; i < 5; i++)
        {
            _engine.BuyMultiple("lemonade", Business.PracticalBatchCap);
            biz.Owned.ShouldBeGreaterThanOrEqualTo(last);
            biz.Owned.ShouldBeGreaterThanOrEqualTo(0L);
            last = biz.Owned;
        }

        biz.Owned.ShouldBeGreaterThan(initial);
    }

    [Fact]
    public async Task CrossBusinessSpeedMultiplier_AtLongRangeMinOwned_StaysFiniteAndPositive()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        foreach (var biz in _engine.Businesses)
            biz.Owned = 3_000_000_000L;

        _engine.MinOwnedAcrossBusinesses.ShouldBe(3_000_000_000L);
        var mult = _engine.CrossBusinessSpeedMultiplier;
        mult.IsFinite.ShouldBeTrue();
        mult.Sign.ShouldBe(1);
        mult.ToDouble().ShouldBe(CrossBusinessSpeedBonus.MaxMultiplier, tolerance: 1e-9);
    }

    [Fact]
    public async Task NextCost_AfterMultipleBulkBuys_NeverCollapsesToSubAtto()
    {
        await _engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(new BigDouble(1.0, 10_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");

        for (var i = 0; i < 4; i++)
            _engine.BuyMultiple("shrimp", 1_000_000_000L);

        shrimp.Owned.ShouldBeGreaterThan(0L);
        shrimp.NextCost.IsFinite.ShouldBeTrue();
        (shrimp.NextCost >= new BigDouble(shrimp.BaseCost)).ShouldBeTrue();
    }

    private void SetCash(BigDouble amount)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        prop.GetSetMethod(true)!.Invoke(_engine, [amount]);
    }
}
