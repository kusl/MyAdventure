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

    /// <summary>Cost to buy the next unit of this business.</summary>
    public double NextCost => BaseCost * Math.Pow(CostMultiplier, Owned);

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
    /// </summary>
    public double Revenue => BaseRevenue * Owned * MilestoneMultiplier * PostMilestoneScaling;

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
    /// </summary>
    public double PostMilestoneScaling =>
        Owned <= 1000 ? 1.0 : Math.Pow(CostMultiplier, (Owned - 1000) / 2.0);

    /// <summary>Cycle time in seconds.</summary>
    public double CycleTimeSeconds => BaseTimeSeconds;

    /// <summary>Revenue per second when running.</summary>
    public double RevenuePerSecond => CycleTimeSeconds > 0 ? Revenue / CycleTimeSeconds : 0;

    /// <summary>
    /// How many units the player can buy with a given cash amount (greedy, one at a time).
    /// </summary>
    public int AffordableCount(double cash)
    {
        var count = 0;
        var simOwned = Owned;
        var remaining = cash;
        while (true)
        {
            var cost = BaseCost * Math.Pow(CostMultiplier, simOwned);
            if (remaining < cost) break;
            remaining -= cost;
            simOwned++;
            count++;
            // Safety cap to avoid infinite loops with tiny multipliers
            if (count > 10_000) break;
        }
        return count;
    }
}
