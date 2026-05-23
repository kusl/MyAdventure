using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// Pins the "multiple cycles per frame" invariant the tick loop relies
/// on. Once a business's effective cycle time drops below a single
/// frame (≈16 ms at 60 Hz), one tick must be able to award credit for
/// many full cycles plus carry the residual into the next tick. If
/// the loop ever regresses to "one cycle per tick max", the late-game
/// progression silently breaks — the player keeps speeding things up
/// but stops getting paid for the extra cycles.
///
/// <para>
/// These tests don't depend on the speed-milestone feature directly —
/// they manufacture an artificially short cycle time by setting
/// <c>BaseTimeSeconds</c> small. That keeps the invariant tested
/// even if the speed-milestone curve is rebalanced in the future.
/// </para>
///
/// <para>
/// <b>Precision-gap caveat (learned the hard way):</b> these tests
/// measure earnings by diffing <c>engine.Cash</c> before and after a
/// tick. <see cref="BigDouble"/>'s mantissa has ~17 digits of precision,
/// so if starting cash is set far above per-tick earnings (e.g. 10^300
/// vs $665K earnings), the addition is absorbed into the precision gap
/// and the diff comes out as zero. Starting cash must therefore stay
/// within ~15 orders of magnitude of the per-tick earnings being
/// measured. We deliberately start near zero in these tests and rely
/// on the engine's <c>SanitizeMoney</c> to never produce negative cash.
/// </para>
/// </summary>
public class SubFrameCycleTests
{
    private static readonly IGameStateRepository Repo = MakeRepo();

    private static IGameStateRepository MakeRepo()
    {
        var r = Substitute.For<IGameStateRepository>();
        r.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<GameState?>(null));
        return r;
    }

    /// <summary>
    /// The invariant: a single 1-second tick over a 1-ms cycle time
    /// must pay out for ~1000 cycles, not just 1. The doc warned that
    /// a naïve "if progress &gt;= 100 then award one cycle" loop would
    /// drop the extra 999. The current engine correctly takes
    /// <c>cycles = (int)(progress / 100)</c> and pays for all of them.
    /// </summary>
    [Fact]
    public async Task Tick_FastCycleTime_AwardsManyCyclesInOneFrame()
    {
        var engine = new GameEngine(Repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync();
        SetCash(engine, BigDouble.Zero);

        // Replace the lemonade stand with a 1-ms-cycle variant via reflection
        // on the Businesses list, then buy 1 unit so it can earn.
        var fastBiz = new Business
        {
            Id = "fast",
            Name = "Fast Test",
            Icon = "⚡",
            Color = "#000",
            BaseCost = 1,
            BaseRevenue = 1.0,
            BaseTimeSeconds = 0.001, // 1 ms per cycle
            CostMultiplier = 1.07,
            Owned = 1,
            IsRunning = true,
            HasManager = true,
            ProgressPercent = 0,
        };
        var bizList = new List<Business>(engine.Businesses) { fastBiz };
        SetBusinesses(engine, bizList);

        var cashBefore = engine.Cash;
        engine.Tick(1.0); // one big tick — simulates a resume or slow frame

        var earned = (engine.Cash - cashBefore).ToDouble();
        // 1 s / 1 ms = 1000 cycles. Each cycle pays $1 (1 owned × $1 base).
        // Allow ±1 cycle slack for the integer-truncation residual that
        // gets carried into ProgressPercent.
        earned.ShouldBeInRange(999.0, 1001.0);
    }

    /// <summary>
    /// The residual carry: after multi-cycle award, the leftover
    /// fraction must persist into <c>ProgressPercent</c> so subsequent
    /// ticks count it. If the engine clamps progress to 0 after award
    /// (rather than <c>%= 100.0</c>), cycle drift accumulates over time.
    /// </summary>
    [Fact]
    public async Task Tick_PartialCycleRemainder_CarriesIntoNextTick()
    {
        var engine = new GameEngine(Repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync();
        SetCash(engine, BigDouble.Zero);

        var biz = new Business
        {
            Id = "carrytest",
            Name = "Carry Test",
            Icon = "🌀",
            Color = "#000",
            BaseCost = 1,
            BaseRevenue = 1.0,
            // 1.5 cycles per tick — leaves a clean 50% residual.
            BaseTimeSeconds = 1.0,
            CostMultiplier = 1.07,
            Owned = 1,
            IsRunning = true,
            HasManager = true,
            ProgressPercent = 0,
        };
        SetBusinesses(engine, new List<Business>(engine.Businesses) { biz });

        // Tick 1.5 s on a 1.0 s cycle — 1 cycle paid, 50% residual carried.
        engine.Tick(1.5);
        biz.ProgressPercent.ShouldBe(50.0, tolerance: 1e-9);

        // Another 0.5 s tick should now complete the second cycle exactly.
        var cashAfterFirstTick = engine.Cash;
        engine.Tick(0.5);
        (engine.Cash - cashAfterFirstTick).ToDouble().ShouldBe(1.0, tolerance: 1e-9);
        biz.ProgressPercent.ShouldBe(0.0, tolerance: 1e-9);
    }

    /// <summary>
    /// Composing this with the speed-milestone feature: at 400 owned,
    /// cycle time is 1/16th of base. With a 0.6 s base (lemonade) the
    /// effective cycle becomes 37.5 ms. A 1 s tick must then pay for
    /// ~26 cycles, not 1.
    ///
    /// <para>
    /// Starting cash here is <see cref="BigDouble.Zero"/> — earlier I
    /// set it to 10^300 thinking "lots of cash so the engine can't get
    /// confused", but that's exactly the precision-gap trap described
    /// in the class summary: <c>(1e300 + 665600) - 1e300</c> rounds
    /// back to zero in BigDouble's 17-digit mantissa, so the diff
    /// measures nothing. With cash starting at 0 the earned amount
    /// sits in its own precision range and is observable.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tick_AtSpeedMilestone400_AwardsExpectedCyclesPerSecond()
    {
        var engine = new GameEngine(Repo, NullLogger<GameEngine>.Instance);
        await engine.LoadAsync();
        SetCash(engine, BigDouble.Zero);

        var biz = new Business
        {
            Id = "speed400",
            Name = "Speed 400",
            Icon = "🏎️",
            Color = "#000",
            BaseCost = 1,
            BaseRevenue = 1.0,
            BaseTimeSeconds = 0.6, // lemonade-like
            CostMultiplier = 1.07,
            Owned = 400, // hits all 4 speed milestones: cycle becomes 0.6 / 16 = 0.0375 s
            IsRunning = true,
            HasManager = true,
            ProgressPercent = 0,
        };
        // Sanity: CycleTimeSeconds applies the speed multiplier
        biz.CycleTimeSeconds.ShouldBe(0.0375, tolerance: 1e-12);
        biz.SpeedMultiplier.ShouldBe(16.0);

        SetBusinesses(engine, new List<Business>(engine.Businesses) { biz });

        var cashBefore = engine.Cash;
        engine.Tick(1.0);

        // 1.0 s / 0.0375 s/cycle ≈ 26.66 cycles → integer floor = 26.
        // Revenue per cycle = 400 owned × $1 base × ×64 milestone (all 6 revenue
        // milestones at 25/50/100/200/300/400 = 2^6 = 64) = $25,600 / cycle.
        var revenuePerCycle = biz.Revenue.ToDouble();
        revenuePerCycle.ShouldBe(400 * 64, tolerance: 1e-9);

        var earned = (engine.Cash - cashBefore).ToDouble();
        // 26 cycles × $25,600/cycle = $665,600. Allow ±1 cycle for residual.
        earned.ShouldBeInRange(25 * revenuePerCycle, 27 * revenuePerCycle);
    }

    // ---------------- Helpers ----------------

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
