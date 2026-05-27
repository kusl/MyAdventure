using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Entities;

/// <summary>
/// Represents a business the player can own in the idle game. Each
/// business earns revenue over a cycle time; revenue is boosted by
/// milestone multipliers and post-milestone scaling, and cycle time is
/// shortened by speed milestones (which keep mid/late game progression
/// from stalling once revenue scaling alone stops being enough).
///
/// <para>
/// <b>BigDouble migration note:</b> historically <see cref="NextCost"/>,
/// <see cref="Revenue"/>, and all monetary properties on this entity were
/// <see cref="double"/> values clamped at <c>1e200</c> — a hard ceiling
/// that produced the "game gets stuck at 10²⁰⁰" symptom once a determined
/// player saturated cash. They are now <see cref="BigDouble"/>, which has
/// no practical ceiling and lets growth continue forever.
/// </para>
///
/// <para>
/// <b>Owned is a <see cref="long"/>, not an <see cref="int"/>.</b> Ownership
/// counts in this game can and do exceed 2³¹. A player who lets prestige
/// stack and bulk-buys at scale will hit ~4 billion units of a single
/// business within an afternoon. Under the prior <c>int</c> field,
/// <see cref="GameEngine.BuyMultiple"/> wrapped at 2³¹: a +1B purchase on
/// top of 3B owned silently became −294,966,296 — corrupting every
/// downstream calculation (revenue went negative, <see cref="NextCost"/>
/// collapsed to <c>BaseCost × CostMultiplier^(huge negative)</c>, i.e. a
/// vanishingly tiny positive, which made every subsequent bulk-buy
/// "free" and accelerated the corruption). <see cref="long"/> gives us
/// 9.2 × 10¹⁸ — at one purchase per microsecond, ~292,000 years. The same
/// "you won't reach this from the Big Bang" criterion the BigDouble
/// comments hold themselves to. Saves are JSON-encoded so the on-disk
/// format absorbs the widening transparently; old saves with int-range
/// values round-trip into <c>long</c> without migration. Hand-edited
/// saves with values past <c>int.MaxValue</c> now load correctly instead
/// of throwing inside <c>JsonElement.GetInt32</c>.
/// </para>
///
/// <para>
/// Definitional inputs (<see cref="BaseCost"/>, <see cref="BaseRevenue"/>,
/// <see cref="BaseTimeSeconds"/>, <see cref="CostMultiplier"/>) stay as
/// <see cref="double"/> because the static balance table only contains
/// small values; this keeps <see cref="BusinessDefinitions"/> readable.
/// The arithmetic that produces unbounded values lifts to <c>BigDouble</c>
/// at the boundary.
/// </para>
/// <para>
/// <b>Speed milestones note:</b> <see cref="CycleTimeSeconds"/> is the
/// <i>effective</i> cycle time after speed milestones, not the raw base.
/// The raw base is preserved at <see cref="BaseTimeSeconds"/> for callers
/// that need to compare against the original balance value. Until
/// ownership crosses the first speed-milestone threshold (100), the two
/// are equal — so every test that constructs a Business with owned &lt; 100
/// sees the same cycle time as before this feature existed.
/// </para>
/// </summary>
public record Business
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string Color { get; init; }

    /// <summary>Definitional base cost of the first unit. Stays a double because the balance table is bounded.</summary>
    public required double BaseCost { get; init; }

    /// <summary>Definitional base revenue per cycle for a single unit owned.</summary>
    public required double BaseRevenue { get; init; }

    /// <summary>
    /// Raw cycle duration in seconds, before any speed-milestone scaling.
    /// This is the value from the static balance table; the actual cycle
    /// time the engine uses is <see cref="CycleTimeSeconds"/>, which
    /// applies <see cref="SpeedMultiplier"/> on top.
    /// </summary>
    public required double BaseTimeSeconds { get; init; }

    /// <summary>Geometric scaling factor on cost per additional unit (e.g. 1.07).</summary>
    public required double CostMultiplier { get; init; }

    /// <summary>
    /// Number of units owned. <see cref="long"/>, not <see cref="int"/> —
    /// see the type-level remark on this record for the overflow history
    /// that drove the widening.
    /// </summary>
    public long Owned { get; set; }

    public bool HasManager { get; set; }
    public double ProgressPercent { get; set; }
    public bool IsRunning { get; set; }

    /// <summary>
    /// Cost to buy the next unit of this business. Computed as
    /// <c>BaseCost × CostMultiplier^Owned</c> using <see cref="BigDouble.Pow(double)"/>
    /// so that even astronomical ownership counts produce a finite,
    /// representable value (the prior <c>Math.Pow</c>-based formulation
    /// overflowed to <see cref="double.PositiveInfinity"/> around
    /// <c>1.11^7000</c> and forced an artificial clamp).
    /// <para>
    /// <see cref="Owned"/> is widened to <see cref="double"/> for the
    /// exponent because <see cref="BigDouble.Pow(double)"/> takes a
    /// double. Precision loss in that conversion only matters past
    /// 2⁵³ ≈ 9 × 10¹⁵ owned, at which point unit-level cost precision is
    /// already invisible relative to the geometric growth.
    /// </para>
    /// </summary>
    public BigDouble NextCost =>
        new BigDouble(BaseCost) * new BigDouble(CostMultiplier).Pow((double)Owned);

    /// <summary>
    /// Revenue per cycle with current units owned, including the
    /// compounded milestone multiplier and post-milestone scaling.
    /// <see cref="PostMilestoneScaling"/> is exactly 1.0 below 1000
    /// owned, so the early-game balance is unchanged.
    /// </summary>
    public BigDouble Revenue =>
        new BigDouble(BaseRevenue) * Owned * MilestoneMultiplier * PostMilestoneScaling;

    /// <summary>Compounded milestone multiplier (×2 / ×4 / ×5 stacks based on ownership thresholds).</summary>
    public double MilestoneMultiplier => Milestone.CalculateMultiplier(Owned);

    /// <summary>
    /// Compounded speed multiplier from speed milestones. ≥ 1.0 — a
    /// value of 2.0 means cycles fire twice as often. Below the first
    /// speed-milestone threshold this is exactly 1.0, so early-game
    /// balance and all pre-milestone tests are unaffected.
    /// </summary>
    public double SpeedMultiplier => SpeedMilestone.CalculateSpeedMultiplier(Owned);

    /// <summary>
    /// Past the 1000-unit milestone cap, each additional unit costs
    /// <c>CostMultiplier^N</c> more than the unit before it but contributes
    /// the same revenue per unit. To stop the mid-game from stalling, we
    /// multiply revenue by <c>CostMultiplier^((Owned - 1000) / 2)</c> past
    /// the cap — the square root of cost growth means unit 1001's
    /// cost-to-payback ratio matches unit 1000's, keeping purchases
    /// efficient indefinitely.
    /// <para>
    /// Below 1000, this is exactly 1.0 — early/mid-game balance and
    /// every pre-cap test are unaffected.
    /// </para>
    /// </summary>
    public BigDouble PostMilestoneScaling
    {
        get
        {
            if (Owned <= 1000) return BigDouble.One;
            // (Owned - 1000) / 2.0 widens to double via Owned cast. Even
            // at long.MaxValue this is well within double's exponent
            // range; precision past 2⁵³ doesn't matter for the same
            // reason it doesn't matter in NextCost.
            return new BigDouble(CostMultiplier).Pow((Owned - 1000L) / 2.0);
        }
    }

    /// <summary>
    /// Effective cycle time in seconds after applying speed milestones.
    /// This is what the engine's tick loop and offline-earnings
    /// calculation use. Equals <see cref="BaseTimeSeconds"/> below the
    /// first speed-milestone threshold so pre-milestone balance is
    /// untouched.
    /// </summary>
    public double CycleTimeSeconds =>
        BaseTimeSeconds * SpeedMilestone.CalculateCycleTimeMultiplier(Owned);

    /// <summary>Revenue per second when running, using the effective (post-speed) cycle time.</summary>
    public BigDouble RevenuePerSecond =>
        CycleTimeSeconds > 0 ? Revenue / new BigDouble(CycleTimeSeconds) : BigDouble.Zero;

    /// <summary>
    /// How many units the player can buy with a given cash amount, using the
    /// geometric-series closed form rather than a brute-force loop.
    /// <para>
    /// For a geometric purchase sequence with first cost
    /// <c>c₀ = BaseCost × CostMultiplier^Owned</c> and ratio
    /// <c>r = CostMultiplier</c>, the cumulative cost of <c>n</c> purchases is
    /// <c>c₀ × (rⁿ - 1) / (r - 1)</c>. Solving the inequality
    /// <c>c₀ × (rⁿ - 1) / (r - 1) ≤ cash</c> for the largest integer
    /// <c>n</c> gives the affordable count.
    /// </para>
    /// <para>
    /// The closed form is what makes "buy max" practical even when the
    /// affordable count is, say, 50,000 — the previous loop-with-safety-cap
    /// terminated at 10,000 purchases (and would take milliseconds even
    /// then). The closed form is O(1) regardless of how many units the
    /// player can afford.
    /// </para>
    /// <para>
    /// <b>Return type is <see cref="long"/>.</b> The prior <c>int</c>
    /// return type and 1-billion safety cap were the proximate cause of
    /// the wraparound exploit: a player with ~3B units of a business
    /// would request a billion more, the call would succeed, and
    /// <see cref="GameEngine.BuyMultiple"/> would add it to an int Owned
    /// field that promptly overflowed to negative. The cap is still
    /// here — long-range arithmetic is happy to compute arbitrary
    /// affordable counts, but we don't want a single batch purchase to
    /// take ten billion units in one go for UX/observability reasons.
    /// The cap is now <c>long.MaxValue / 4</c>, far past any reasonable
    /// batch, while still leaving headroom for downstream arithmetic
    /// (<c>Owned + cap</c>) to never overflow.
    /// </para>
    /// </summary>
    public long AffordableCount(BigDouble cash)
    {
        if (cash.IsNaN || cash.Sign <= 0) return 0L;
        // Infinite cash: return the practical cap. This shouldn't appear
        // in normal gameplay (the engine sanitizes Infinity → 0 on every
        // monetary path), but a defensive answer beats returning 0 from
        // the log-based formula below, which would interpret +∞ → log10(+∞) → +∞
        // → !IsFinite → return 0.
        if (cash.IsInfinity) return PracticalBatchCap;

        if (BaseCost <= 0 || CostMultiplier <= 1.0)
        {
            // Defensive: a non-positive base cost or a non-increasing
            // multiplier would make the analytic formula misbehave. The
            // production balance table never hits these cases, but tests
            // exercising "free" or unusual businesses can.
            if (BaseCost <= 0) return PracticalBatchCap;

            // CostMultiplier == 1.0: every unit costs the same as the
            // first, so affordable count is plain floor(cash / NextCost).
            var perUnit = NextCost;
            if (perUnit.IsZero) return PracticalBatchCap;
            var rawCount = cash / perUnit;
            var asDouble = rawCount.ToDouble();
            if (!double.IsFinite(asDouble) || asDouble <= 0) return 0L;
            return (long)Math.Min(asDouble, (double)PracticalBatchCap);
        }

        // First-unit cost at the current owned count.
        var c0 = NextCost;
        if (c0.IsZero) return PracticalBatchCap;
        if (cash < c0) return 0L;

        // Maximum n satisfying c0 × (r^n - 1) / (r - 1) ≤ cash, i.e.
        //   r^n ≤ 1 + cash × (r - 1) / c0
        //   n   ≤ log_r(1 + cash × (r - 1) / c0)
        // We compute the right-hand side as a double — even very large
        // affordable counts are well within double's precision because
        // it's a log.
        var r = CostMultiplier;
        var threshold = BigDouble.One + cash * new BigDouble(r - 1.0) / c0;

        // Convert to a double for the final log; log10 of a BigDouble
        // is always representable as a double (it's just an exponent + a tiny mantissa log).
        var logThreshold = threshold.Log10();
        if (!double.IsFinite(logThreshold) || logThreshold <= 0) return 0L;

        var n = Math.Floor(logThreshold / Math.Log10(r));

        // Cap at the practical batch limit (still long-range; see remarks
        // on the property). 1e9 used to be the cap and is well past any
        // human "buy max" intent for a single batch.
        if (n <= 0) return 0L;
        if (n >= (double)PracticalBatchCap) return PracticalBatchCap;
        return (long)n;
    }

    /// <summary>
    /// Per-call cap on <see cref="AffordableCount"/> and
    /// <see cref="GameEngine.BuyMultiple"/>. Kept well below
    /// <see cref="long.MaxValue"/> so that <c>Owned + cap</c> can never
    /// overflow even when called repeatedly against an already-huge
    /// ownership count. <c>long.MaxValue / 4</c> ≈ 2.3 × 10¹⁸ — at one
    /// batch per microsecond, ~73,000 years to overflow even by
    /// chaining max-cap batches back to back.
    /// </summary>
    public const long PracticalBatchCap = long.MaxValue / 4;
}
