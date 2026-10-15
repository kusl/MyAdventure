using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Entities;

public record Business
{
    public const long PracticalBatchCap = long.MaxValue / 4;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string Color { get; init; }
    public required double BaseCost { get; init; }
    public required double BaseRevenue { get; init; }
    public required double BaseTimeSeconds { get; init; }
    public required double CostMultiplier { get; init; }

    public long Owned { get; set; }
    public bool HasManager { get; set; }
    public double ProgressPercent { get; set; }
    public bool IsRunning { get; set; }

    public BigDouble NextCost =>
        new BigDouble(BaseCost) * new BigDouble(CostMultiplier).Pow((double)Owned);

    public BigDouble Revenue =>
        new BigDouble(BaseRevenue) * Owned * MilestoneMultiplier * Efficiency;

    public double MilestoneMultiplier => Milestone.CalculateMultiplier(Owned);

    public double SpeedMultiplier => SpeedMilestone.CalculateSpeedMultiplier(Owned);

    public BigDouble Efficiency => GameBalance.Efficiency(Owned, CostMultiplier);

    public double CycleTimeSeconds =>
        BaseTimeSeconds * SpeedMilestone.CalculateCycleTimeMultiplier(Owned);

    public BigDouble RevenuePerSecond =>
        CycleTimeSeconds > 0 ? Revenue / new BigDouble(CycleTimeSeconds) : BigDouble.Zero;

    public BigDouble CostOf(long count)
    {
        if (count <= 0L) return BigDouble.Zero;
        if (CostMultiplier == 1.0) return NextCost * count;
        var rPowN = new BigDouble(CostMultiplier).Pow((double)count);
        return NextCost * (rPowN - BigDouble.One) / new BigDouble(CostMultiplier - 1.0);
    }

    public long AffordableCount(BigDouble cash)
    {
        if (cash.IsNaN || cash.Sign <= 0) return 0L;
        if (cash.IsInfinity) return PracticalBatchCap;

        if (BaseCost <= 0 || CostMultiplier <= 1.0)
        {
            if (BaseCost <= 0) return PracticalBatchCap;
            var perUnit = NextCost;
            if (perUnit.IsZero) return PracticalBatchCap;
            var asDouble = (cash / perUnit).ToDouble();
            if (!double.IsFinite(asDouble) || asDouble <= 0) return 0L;
            return (long)Math.Min(asDouble, (double)PracticalBatchCap);
        }

        var c0 = NextCost;
        if (c0.IsZero) return PracticalBatchCap;
        if (cash < c0) return 0L;

        var r = CostMultiplier;
        var threshold = BigDouble.One + cash * new BigDouble(r - 1.0) / c0;
        var logThreshold = threshold.Log10();
        if (!double.IsFinite(logThreshold) || logThreshold <= 0) return 0L;

        var n = Math.Floor(logThreshold / Math.Log10(r));
        if (n <= 0) return 0L;
        if (n >= (double)PracticalBatchCap) return PracticalBatchCap;
        return (long)n;
    }
}
