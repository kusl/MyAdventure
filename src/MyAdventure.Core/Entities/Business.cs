namespace MyAdventure.Core.Entities;

/// <summary>
/// Represents a business the player can own in the idle game.
/// Each business earns revenue over a cycle time.
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

    /// <summary>Revenue per cycle with current units owned.</summary>
    public double Revenue => BaseRevenue * Owned;

    /// <summary>Cycle time in seconds (does not change with upgrades in v1).</summary>
    public double CycleTimeSeconds => BaseTimeSeconds;
}
