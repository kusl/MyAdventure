using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Entities;

public static class GameBalance
{
    public const double StartingCash = 5.0;
    public const double ManagerCostMultiplier = 1000.0;
    public const double EfficiencyExponent = 0.49;
    public const long EfficiencyNormalizationOwned = 1000;
    public const double AngelBonusPerAngel = 0.02;
    public const double AngelScale = 150.0;
    public const double AngelLifetimeDivisor = 1e14;
    public const double PrestigeMinLifetime = 1e12;
    public const double CrossBusinessMultiplierPerThreshold = 1.5;

    public static double MilestoneLog10AtNormalization { get; } =
        Math.Log10(Milestone.CalculateMultiplier(EfficiencyNormalizationOwned)) +
        Math.Log10(SpeedMilestone.CalculateSpeedMultiplier(EfficiencyNormalizationOwned));

    public static double EfficiencyLog10(long owned, double costMultiplier)
    {
        if (owned <= 0 || !(costMultiplier > 1.0)) return 0.0;
        var n = (double)owned;
        var normalized = Math.Min(n, EfficiencyNormalizationOwned) / EfficiencyNormalizationOwned;
        var log = EfficiencyExponent * n * Math.Log10(costMultiplier) - normalized * MilestoneLog10AtNormalization;
        return log > 0.0 ? log : 0.0;
    }

    public static BigDouble Efficiency(long owned, double costMultiplier)
    {
        var log = EfficiencyLog10(owned, costMultiplier);
        return log <= 0.0 ? BigDouble.One : new BigDouble(10.0).Pow(log);
    }

    public static BigDouble AngelBonusFor(BigDouble angels)
    {
        if (angels.IsNaN || angels.IsInfinity || angels.Sign <= 0) return BigDouble.One;
        return BigDouble.One + angels * new BigDouble(AngelBonusPerAngel);
    }

    public static BigDouble ManagerCost(Business business) =>
        new(business.BaseCost * ManagerCostMultiplier);
}
