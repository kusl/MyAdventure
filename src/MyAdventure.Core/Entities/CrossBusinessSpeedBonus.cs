using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Entities;

public static class CrossBusinessSpeedBonus
{
    public static IReadOnlyList<long> Thresholds { get; } = [25L, 50L, 100L, 200L, 300L, 400L];

    public static long MaxBonusCount => Thresholds.Count;

    public static double MaxMultiplier { get; } =
        Math.Pow(GameBalance.CrossBusinessMultiplierPerThreshold, Thresholds.Count);

    public static long BonusCount(long minOwnedAcrossBusinesses)
    {
        var count = 0L;
        foreach (var t in Thresholds)
        {
            if (minOwnedAcrossBusinesses >= t) count++;
        }
        return count;
    }

    public static BigDouble CalculateSpeedMultiplier(long minOwnedAcrossBusinesses)
    {
        var count = BonusCount(minOwnedAcrossBusinesses);
        return count == 0L
            ? BigDouble.One
            : new BigDouble(Math.Pow(GameBalance.CrossBusinessMultiplierPerThreshold, count));
    }

    public static long? NextThreshold(long minOwnedAcrossBusinesses)
    {
        foreach (var t in Thresholds)
        {
            if (minOwnedAcrossBusinesses < t) return t;
        }
        return null;
    }

    public static long UnitsToNext(long minOwnedAcrossBusinesses)
    {
        var next = NextThreshold(minOwnedAcrossBusinesses);
        if (next is null) return 0L;
        var diff = next.Value - minOwnedAcrossBusinesses;
        return diff < 0L ? 0L : diff;
    }
}
