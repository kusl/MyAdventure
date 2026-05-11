namespace MyAdventure.Core.Entities;

/// <summary>
/// Represents a business the player can own in the idle game.
/// Each business earns revenue over a cycle time.
/// Revenue is boosted by milestone multipliers and post-milestone
/// scaling (which keeps unit purchases worthwhile after the
/// milestone table caps out at 1000 owned).
/// </summary>
public record Business
{
    /// <summary>
    /// Hard cap on any double value derived inside this entity (cost,
    /// revenue, scaling). Chosen well below <see cref="double.MaxValue"/>
    /// (~1.8e308) so that one or two further multiplications downstream
    /// can't push the value to <see cref="double.PositiveInfinity"/>.
    /// <para>
    /// Caps here are belt-and-braces with the engine-level
    /// <c>SanitizeMoney</c>: even if a derived value momentarily exceeds
    /// the cap, the engine clamps before persisting it.
    /// </para>
    /// </summary>
    private const double MaxFiniteValue = 1e200;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string Color { get; init; }
    public required double BaseCost { get; init; }
    public required double BaseRevenue { get; init; }
    public required double BaseTimeSeconds { get; init; }
    public required double CostMultiplier { get; init; }
    public int Owned { get; set; }
    public bool HasManager { get; set; }
    public double ProgressPercent { get; set; }
    public bool IsRunning { get; set; }

    /// <summary>
    /// Cost to buy the next unit of this business.
    /// <para>
    /// At very high <see cref="Owned"/> values <c>Math.Pow(CostMultiplier, Owned)</c>
    /// can overflow to <see cref="double.PositiveInfinity"/> (e.g. <c>1.11^7000</c>
    /// exceeds <see cref="double.MaxValue"/>). The clamp keeps the cost
    /// finite so callers like <c>BuyBusiness</c> can compare against
    /// <c>Cash</c> without producing NaN. Past the clamp the business is
    /// effectively unaffordable, which is the desired behavior anyway.
    /// </para>
    /// </summary>
    public double NextCost
    {
        get
        {
            var raw = BaseCost * Math.Pow(CostMultiplier, Owned);
            if (!double.IsFinite(raw)) return MaxFiniteValue;
            return Math.Min(raw, MaxFiniteValue);
        }
    }

    /// <summary>
    /// Revenue per cycle with current units owned, including the
    /// compounded milestone multiplier and post-milestone scaling.
    /// <para>
    /// Below 1000 owned, <see cref="PostMilestoneScaling"/> is exactly
    /// 1.0 — early/mid-game players see no behavior change. Past the
    /// 1000-unit milestone cap, scaling kicks in to compensate for the
    /// continuing exponential cost growth (see the property summary
    /// for the full rationale).
    /// </para>
    /// <para>
    /// Clamped to <see cref="MaxFiniteValue"/> so an extreme combination
    /// of milestone multiplier, owned count and post-milestone scaling
    /// can't return Infinity. Without this, a multiplication by the
    /// AngelBonus downstream could turn finite cash into Infinity and
    /// cascade into the JSON-export crash.
    /// </para>
    /// </summary>
    public double Revenue
    {
        get
        {
            var raw = BaseRevenue * Owned * MilestoneMultiplier * PostMilestoneScaling;
            if (!double.IsFinite(raw)) return MaxFiniteValue;
            return Math.Min(raw, MaxFiniteValue);
        }
    }

    /// <summary>Current combined milestone multiplier.</summary>
    public double MilestoneMultiplier => Milestone.CalculateMultiplier(Owned);

    /// <summary>
    /// Past the 1000-unit milestone cap, each additional unit costs
    /// <c>CostMultiplier^N</c> more than the unit before it, but
    /// previously contributed the same revenue per unit as unit 1000.
    /// That is the geometry that produces a "stuck" mid-game where
    /// the next unit costs trillions and pays back in centuries.
    /// <para>
    /// The fix: past 1000, multiply revenue by
    /// <c>CostMultiplier^((Owned - 1000) / 2)</c>. The square root of
    /// cost growth means the cost-to-payback ratio of unit 1001 is
    /// roughly the same as unit 1000 — buying past the cap stays
    /// efficient instead of decaying exponentially.
    /// </para>
    /// <para>
    /// Below 1000, this is exactly 1.0 — early/mid-game progression
    /// (and all balance tests written against pre-cap units) are
    /// unaffected. Save compatibility is preserved because nothing
    /// here is persisted; it's a function of <see cref="Owned"/>.
    /// </para>
    /// <para>
    /// Clamped against overflow for the same reason as <see cref="Revenue"/>:
    /// keeping every derived value finite is what guarantees the rest
    /// of the engine doesn't have to handle Infinity inputs.
    /// </para>
    /// </summary>
    public double PostMilestoneScaling
    {
        get
        {
            if (Owned <= 1000) return 1.0;
            var raw = Math.Pow(CostMultiplier, (Owned - 1000) / 2.0);
            if (!double.IsFinite(raw)) return MaxFiniteValue;
            return Math.Min(raw, MaxFiniteValue);
        }
    }

    /// <summary>Cycle time in seconds.</summary>
    public double CycleTimeSeconds => BaseTimeSeconds;

    /// <summary>Revenue per second when running.</summary>
    public double RevenuePerSecond => CycleTimeSeconds > 0 ? Revenue / CycleTimeSeconds : 0;

    /// <summary>
    /// How many units the player can buy with a given cash amount (greedy, one at a time).
    /// </summary>
    public int AffordableCount(double cash)
    {
        if (!double.IsFinite(cash) || cash <= 0) return 0;
        var count = 0;
        var simOwned = Owned;
        var remaining = cash;
        while (true)
        {
            var cost = BaseCost * Math.Pow(CostMultiplier, simOwned);
            if (!double.IsFinite(cost) || remaining < cost) break;
            remaining -= cost;
            simOwned++;
            count++;
            // Safety cap to avoid infinite loops with tiny multipliers
            if (count > 10_000) break;
        }
        return count;
    }
}
