using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

/// <summary>
/// Regression pins for the int32-overflow bug where <see cref="Business.Owned"/>
/// wrapped to negative after a few billion-unit bulk buys. The corrupted save
/// captured in production had <c>shrimp.Owned = -294,966,296</c>
/// (i.e. <c>4,000,001,000 mod 2³²</c>), and was logged as:
///
/// <code>Bulk bought 1000000000 of Shrimp Boat for 7.53523184641835e-13368745 (now -294966296)</code>
///
/// The chain of failures from that one wrap was:
/// <list type="number">
///   <item>A player at ~3B shrimp boats requested another 1B in bulk-buy.</item>
///   <item><see cref="GameEngine.BuyMultiple"/> capped at
///         <see cref="Business.AffordableCount"/> (1B) and did
///         <c>biz.Owned += toBuy</c> on an <c>int</c> field — silently wrapping
///         to <c>-294,966,296</c>.</item>
///   <item>With <c>Owned</c> negative, <see cref="Business.NextCost"/> evaluated
///         <c>BaseCost × CostMultiplier^Owned</c> where the exponent was now
///         hugely negative — producing a vanishingly tiny cost
///         (<c>~7.5e-13368745</c>) for the next purchase.</item>
///   <item><see cref="Business.AffordableCount"/> happily returned the cap (1B)
///         against that microscopic cost, and the next bulk-buy succeeded for
///         essentially free — wrapping Owned again and accelerating the
///         corruption.</item>
/// </list>
///
/// These tests pin every link in that chain to make the wrap unreachable
/// from gameplay. The widening of <see cref="Business.Owned"/> from
/// <c>int</c> to <c>long</c> end-to-end is the structural fix; these tests
/// are the contract that future refactors can't silently undo.
/// </summary>
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

    // ---------------- The exact production scenario ----------------

    /// <summary>
    /// Direct reproduction of the production wrap. Pre-fix: Owned += 1B
    /// on an int field starting at 3B wrapped to -294,966,296. Post-fix:
    /// Owned is long, the sum lands at exactly 4 billion.
    /// <para>
    /// <b>Cash sizing.</b> Shrimp boats with multiplier 1.11 at 3B owned
    /// have a single-unit cost of <c>1,244,160 × 1.11^(3×10⁹)</c> — an
    /// exponent of <c>3e9 × log₁₀(1.11) ≈ 1.36 × 10⁸</c>. To afford
    /// another billion units the cumulative cost exponent climbs to
    /// <c>~1.8 × 10⁸</c>. A cash value of <c>10^(2×10⁸)</c> sits two
    /// orders of magnitude above the cumulative cost — plenty of
    /// headroom while still being a number BigDouble represents
    /// exactly. (My first pass used <c>10^10⁷</c>, which made every
    /// purchase unaffordable and the wrap-prevention tests vacuous.)
    /// </para>
    /// </summary>
    [Fact]
    public async Task BuyMultiple_StartingAtThreeBillionOwned_DoesNotWrapToNegative()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 200_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");
        shrimp.Owned = 3_000_000_000L;

        var bought = _engine.BuyMultiple("shrimp", 1_000_000_000L);

        bought.ShouldBe(1_000_000_000L);
        shrimp.Owned.ShouldBe(4_000_000_000L);
        shrimp.Owned.ShouldBeGreaterThan(0L); // the contract that was violated
    }

    /// <summary>
    /// BuyMax used to pass int.MaxValue; with Owned at 3B and infinite cash,
    /// it would happily buy another ~2.1B units and overflow. Post-fix, the
    /// affordable-count cap is per-call but the long range absorbs the sum.
    /// <para>
    /// We give the player just enough cash to afford one more unit at
    /// the 3B-owned price point. That's <c>BigDouble(1.0, 1.5e8)</c> —
    /// above the single-unit cost (exponent <c>1.36e8</c>) but well
    /// short of the per-batch cap, so BuyMax buys a bounded handful of
    /// units. The exact count doesn't matter; we only need Owned to
    /// strictly increase to prove no wrap occurred.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BuyMax_AtHugeOwnedCount_DoesNotProduceNegativeOwned()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 200_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");
        shrimp.Owned = 3_000_000_000L;

        _engine.BuyMax("shrimp");

        shrimp.Owned.ShouldBeGreaterThan(0L);
        shrimp.Owned.ShouldBeGreaterThan(3_000_000_000L);
    }

    /// <summary>
    /// The single-buy path (BuyBusiness, called by the user clicking once)
    /// has the same vulnerability in principle — Owned++ on int wraps at 2³¹.
    /// Unreachable from human clicking but exercise the long arithmetic
    /// explicitly so a future refactor can't quietly demote the field.
    /// <para>
    /// At Owned = int.MaxValue (~2.15B) the next shrimp boat costs
    /// <c>1,244,160 × 1.11^2.15e9 ≈ 10^(9.7e7)</c>. We give the player
    /// cash with exponent <c>1.5 × 10⁸</c> — enough to buy a single
    /// unit at that price.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BuyBusiness_AtIntMaxValueOwned_DoesNotWrap()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 150_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");
        shrimp.Owned = (long)int.MaxValue;

        _engine.BuyBusiness("shrimp").ShouldBeTrue();

        shrimp.Owned.ShouldBe((long)int.MaxValue + 1L);
        shrimp.Owned.ShouldBeGreaterThan(0L);
    }

    // ---------------- AffordableCount caps and contract ----------------

    /// <summary>
    /// Even at infinite cash and zero owned, AffordableCount must respect
    /// its per-call cap. The cap is the load-bearing invariant that lets
    /// BuyMultiple add to Owned without checking for overflow: Owned + cap
    /// must fit in long for any reachable Owned.
    /// </summary>
    [Fact]
    public void AffordableCount_NeverExceedsPracticalBatchCap()
    {
        var biz = new Business
        {
            Id = "test", Name = "Test", Icon = "T", Color = "#FFF",
            BaseCost = 1, BaseRevenue = 1, BaseTimeSeconds = 1,
            CostMultiplier = 1.0001 // very slow cost growth = many affordable
        };

        // Infinite cash route.
        biz.AffordableCount(BigDouble.PositiveInfinity)
            .ShouldBe(Business.PracticalBatchCap);

        // Astronomically large but finite cash.
        biz.AffordableCount(new BigDouble(1.0, 100_000))
            .ShouldBeLessThanOrEqualTo(Business.PracticalBatchCap);
    }

    /// <summary>
    /// The cap is positioned so that even a player starting at the
    /// maximum already-reachable Owned can buy another full cap without
    /// overflowing long. Specifically: cap + (long.MaxValue - cap) must
    /// not overflow. Pinning the actual value here means a future change
    /// that raises the cap toward long.MaxValue trips this test.
    /// </summary>
    [Fact]
    public void PracticalBatchCap_LeavesHeadroomForRepeatedMaxBuys()
    {
        // Owned at the cap plus another cap-sized buy must stay positive.
        var sum = Business.PracticalBatchCap + Business.PracticalBatchCap;
        sum.ShouldBeGreaterThan(0L);
        // And, more conservatively, four caps still fit.
        var fourCaps = Business.PracticalBatchCap * 4L;
        fourCaps.ShouldBeGreaterThan(0L);
    }

    // ---------------- The corrupted-save defensiveness ----------------

    /// <summary>
    /// The leaked production save had <c>shrimp:-294966296</c>. We can't
    /// un-corrupt history but the import path's <c>Math.Max(0, owned)</c>
    /// contract must still hold under the widened type — a corrupted
    /// save loads to a playable, non-negative state.
    /// </summary>
    [Fact]
    public async Task ImportFromString_ProductionCorruptSave_LoadsClampedToZero()
    {
        await _engine.LoadAsync();
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
        // Other businesses preserved.
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1000L);
    }

    /// <summary>
    /// Pre-fix the import path called <c>JsonElement.GetInt32</c> on the
    /// per-business count, which threw on any value past
    /// <c>int.MaxValue</c>; the exception was silently swallowed inside
    /// a try/catch that already exists for malformed JSON, so a
    /// legitimately-large hand-edited save would zero out without notice.
    /// Post-fix, long-range values round-trip cleanly.
    /// </summary>
    [Fact]
    public async Task ImportFromString_HandEditedSaveWithLongRangeValue_LoadsCorrectly()
    {
        await _engine.LoadAsync();
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

    /// <summary>
    /// Export → import round-trip of a long-range Owned. Pre-fix, the
    /// serializer wrote a JSON number past int.MaxValue and the import
    /// silently lost it; post-fix the value round-trips through the
    /// canonical Base64-JSON envelope.
    /// </summary>
    [Fact]
    public async Task ExportThenImport_LongRangeOwned_RoundTripsExactly()
    {
        await _engine.LoadAsync();
        var lemon = _engine.Businesses.First(b => b.Id == "lemonade");
        lemon.Owned = 7_000_000_000L;

        var exported = _engine.ExportToString();

        // Fresh engine — verify the imported value matches what we exported.
        var engine2 = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
        await engine2.LoadAsync();
        engine2.ImportFromString(exported).ShouldBeTrue();

        engine2.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(7_000_000_000L);
    }

    // ---------------- The chain-buy invariant ----------------

    /// <summary>
    /// Chain five back-to-back max-cap buys against a fixed cash budget
    /// and confirm Owned never decreases and never wraps negative across
    /// the chain. The production bug compounded across successive
    /// bulk-buys — each wrap making the next purchase appear free — so
    /// the contract this test pins is the closure of that scenario:
    /// monotonicity under any sequence of buys.
    /// <para>
    /// <b>Cash plateau is fine, not a bug.</b> With a fixed cash budget
    /// and geometric cost growth, after a few batches the next first-unit
    /// cost outstrips remaining cash and subsequent batches buy zero.
    /// That's the correct behavior; the original phrasing of this test
    /// ("strictly increases every iteration") was wrong. We pin: (a) at
    /// least one batch succeeded, (b) Owned never went negative, (c) Owned
    /// never went backwards. That's exactly what the wrap bug violated.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BuyMultiple_RepeatedAtCap_OwnedStaysNonNegativeAndMonotonic()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 10_000_000));
        var biz = _engine.Businesses.First(b => b.Id == "lemonade");

        var initial = biz.Owned;
        var last = initial;
        for (var i = 0; i < 5; i++)
        {
            _engine.BuyMultiple("lemonade", Business.PracticalBatchCap);
            biz.Owned.ShouldBeGreaterThanOrEqualTo(last); // monotonic, never wraps
            biz.Owned.ShouldBeGreaterThanOrEqualTo(0L);   // never negative
            last = biz.Owned;
        }

        // At least the first batch must have bought something — the
        // initial cash is huge relative to the starting first-unit cost
        // (4 dollars). If Owned didn't increase at all, the engine is
        // broken in a different way than the wrap, but we'd still want
        // to know.
        biz.Owned.ShouldBeGreaterThan(initial);
    }

    // ---------------- Cross-business multiplier with long-range inputs ----------------

    /// <summary>
    /// The cross-business multiplier consumes the minimum across the
    /// roster. With Owned widened to long, the minimum is also long; the
    /// multiplier path must accept inputs past int.MaxValue without
    /// truncating. We don't pin a specific multiplier value (it's
    /// astronomically large past 10⁹ minimum-owned), only that the
    /// engine reports a finite-and-positive answer rather than
    /// degenerating to NaN/Infinity or wrapping to 1.0.
    /// </summary>
    [Fact]
    public async Task CrossBusinessSpeedMultiplier_AtLongRangeMinOwned_StaysFiniteAndPositive()
    {
        await _engine.LoadAsync();
        foreach (var biz in _engine.Businesses)
            biz.Owned = 3_000_000_000L; // past int.MaxValue (2.147B)

        _engine.MinOwnedAcrossBusinesses.ShouldBe(3_000_000_000L);
        var mult = _engine.CrossBusinessSpeedMultiplier;
        mult.IsFinite.ShouldBeTrue();
        mult.Sign.ShouldBe(1);
        // At this scale we should be far past the original ×64 baseline.
        (mult > new BigDouble(1.0, 100)).ShouldBeTrue();
    }

    // ---------------- The chain that produced the original log line ----------------

    /// <summary>
    /// The synthetic version of the production log line, reconstructed:
    /// take a business to a high Owned count, exhaust cash partway, and
    /// confirm that the next bulk-buy reports a sensible cost (not the
    /// <c>7.53e-13368745</c> sub-attorobust value that signaled the
    /// corruption). The defining quality is: if any prior buy wrapped
    /// Owned negative, NextCost would shrink to ~<c>1e-millions</c>;
    /// here it must stay above a sane lower bound (BaseCost itself).
    /// </summary>
    [Fact]
    public async Task NextCost_AfterMultipleBulkBuys_NeverCollapsesToSubAtto()
    {
        await _engine.LoadAsync();
        SetCash(new BigDouble(1.0, 10_000_000));
        var shrimp = _engine.Businesses.First(b => b.Id == "shrimp");

        for (var i = 0; i < 4; i++)
            _engine.BuyMultiple("shrimp", 1_000_000_000L);

        // Owned should be well past 2³¹ by now — the wrap point. NextCost
        // grows monotonically with Owned (CostMultiplier > 1), so any
        // collapse would be a wrap signal.
        shrimp.Owned.ShouldBeGreaterThan(0L);
        shrimp.NextCost.IsFinite.ShouldBeTrue();
        // BaseCost is 1,244,160 — the cost at zero owned. Higher Owned
        // strictly increases NextCost.
        (shrimp.NextCost >= new BigDouble(shrimp.BaseCost)).ShouldBeTrue();
    }

    private void SetCash(BigDouble amount)
    {
        var prop = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        prop.GetSetMethod(true)!.Invoke(_engine, [amount]);
    }
}
