using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class SubFrameCycleTests
{
    private static readonly IGameStateRepository Repo = MakeRepo();

    private static IGameStateRepository MakeRepo()
    {
        var r = Substitute.For<IGameStateRepository>();
        r.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<GameState?>(null));
        return r;
    }

    [Fact]
    public async Task Tick_FastCycleTime_AwardsManyCyclesInOneFrame()
    {
        var engine = new GameEngine(Repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(engine, BigDouble.Zero);

        var fastBiz = new Business
        {
            Id = "fast",
            Name = "Fast Test",
            Icon = "⚡",
            Color = "#000",
            BaseCost = 1,
            BaseRevenue = 1.0,
            BaseTimeSeconds = 0.001,
            CostMultiplier = 1.01,
            Owned = 1,
            IsRunning = true,
            HasManager = true,
            ProgressPercent = 0,
        };
        var bizList = new List<Business>(engine.Businesses) { fastBiz };
        SetBusinesses(engine, bizList);

        var cashBefore = engine.Cash;
        engine.Tick(1.0);

        var earned = (engine.Cash - cashBefore).ToDouble();
        earned.ShouldBeInRange(999.0, 1001.0);
    }

    [Fact]
    public async Task Tick_PartialCycleRemainder_CarriesIntoNextTick()
    {
        var engine = new GameEngine(Repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(engine, BigDouble.Zero);

        var biz = new Business
        {
            Id = "carrytest",
            Name = "Carry Test",
            Icon = "🌀",
            Color = "#000",
            BaseCost = 1,
            BaseRevenue = 1.0,
            BaseTimeSeconds = 1.0,
            CostMultiplier = 1.01,
            Owned = 1,
            IsRunning = true,
            HasManager = true,
            ProgressPercent = 0,
        };
        SetBusinesses(engine, new List<Business>(engine.Businesses) { biz });

        engine.Tick(1.5);
        biz.ProgressPercent.ShouldBe(50.0, tolerance: 1e-9);

        var cashAfterFirstTick = engine.Cash;
        engine.Tick(0.5);
        (engine.Cash - cashAfterFirstTick).ToDouble().ShouldBe(1.0, tolerance: 1e-9);
        biz.ProgressPercent.ShouldBe(0.0, tolerance: 1e-9);
    }

    [Fact]
    public async Task Tick_AtSpeedMilestone400_AwardsExpectedCyclesPerSecond()
    {
        var engine = new GameEngine(Repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync(TestContext.Current.CancellationToken);
        SetCash(engine, BigDouble.Zero);

        var biz = new Business
        {
            Id = "speed400",
            Name = "Speed 400",
            Icon = "🏎️",
            Color = "#000",
            BaseCost = 1,
            BaseRevenue = 1.0,
            BaseTimeSeconds = 0.6,
            CostMultiplier = 1.01,
            Owned = 400,
            IsRunning = true,
            HasManager = true,
            ProgressPercent = 0,
        };
        biz.CycleTimeSeconds.ShouldBe(0.009375, tolerance: 1e-12);
        biz.SpeedMultiplier.ShouldBe(64.0);

        SetBusinesses(engine, new List<Business>(engine.Businesses) { biz });

        engine.MinOwnedAcrossBusinesses.ShouldBe(0);
        engine.CrossBusinessSpeedMultiplier.ToDouble().ShouldBe(1.0);

        var cashBefore = engine.Cash;
        engine.Tick(1.0);

        var revenuePerCycle = biz.Revenue.ToDouble();
        revenuePerCycle.ShouldBe(400 * 64, tolerance: 1e-9);

        var earned = (engine.Cash - cashBefore).ToDouble();
        earned.ShouldBeInRange(105 * revenuePerCycle, 107 * revenuePerCycle);
    }

    private static void SetCash(GameEngine engine, BigDouble amount)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        prop.GetSetMethod(true)!.Invoke(engine, [amount]);
    }

    private static void SetBusinesses(GameEngine engine, IReadOnlyList<Business> businesses)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.Businesses))!;
        prop.GetSetMethod(true)!.Invoke(engine, [businesses]);
    }
}
