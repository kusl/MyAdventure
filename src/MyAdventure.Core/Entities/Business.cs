namespace MyAdventure.Core.Entities;

/// <summary>
/// Represents a business the player can own in the idle game.
/// Each business earns revenue over a cycle time.
/// Revenue is boosted by milestone multipliers.
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
    /// Revenue per cycle with current units owned, including milestone multipliers.
    /// </summary>
    public double Revenue => BaseRevenue * Owned * MilestoneMultiplier;

    /// <summary>Current combined milestone multiplier.</summary>
    public double MilestoneMultiplier => Milestone.CalculateMultiplier(Owned);

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
