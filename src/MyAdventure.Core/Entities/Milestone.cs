namespace MyAdventure.Core.Entities;

public record Milestone(int Threshold, double Multiplier, string Label)
{
    public static IReadOnlyList<Milestone> Defaults { get; } =
    [
        new(25, 2.0, "×2 Revenue"),
        new(50, 2.0, "×2 Revenue"),
        new(100, 2.0, "×2 Revenue"),
        new(200, 2.0, "×2 Revenue"),
        new(300, 2.0, "×2 Revenue"),
        new(400, 2.0, "×2 Revenue"),
        new(500, 4.0, "×4 Revenue"),
        new(600, 4.0, "×4 Revenue"),
        new(700, 4.0, "×4 Revenue"),
        new(800, 4.0, "×4 Revenue"),
        new(900, 4.0, "×4 Revenue"),
        new(1000, 5.0, "×5 Revenue"),
    ];

    public static double CalculateMultiplier(long owned, IReadOnlyList<Milestone>? milestones = null)
    {
        milestones ??= Defaults;
        var mult = 1.0;
        foreach (var m in milestones)
        {
            if (owned >= m.Threshold)
                mult *= m.Multiplier;
        }
        return mult;
    }

    public static Milestone? NextMilestone(long owned, IReadOnlyList<Milestone>? milestones = null)
    {
        milestones ??= Defaults;
        foreach (var m in milestones)
        {
            if (owned < m.Threshold)
                return m;
        }
        return null;
    }

    public static long UnitsToNext(long owned, IReadOnlyList<Milestone>? milestones = null)
    {
        var next = NextMilestone(owned, milestones);
        return next is null ? 0L : next.Threshold - owned;
    }
}

public record SpeedMilestone(int Threshold, double CycleTimeMultiplier, string Label)
{
    public static IReadOnlyList<SpeedMilestone> Defaults { get; } =
    [
        new(25, 0.5, "×2 Speed"),
        new(50, 0.5, "×2 Speed"),
        new(100, 0.5, "×2 Speed"),
        new(200, 0.5, "×2 Speed"),
        new(300, 0.5, "×2 Speed"),
        new(400, 0.5, "×2 Speed"),
    ];

    public static double CalculateCycleTimeMultiplier(long owned, IReadOnlyList<SpeedMilestone>? milestones = null)
    {
        milestones ??= Defaults;
        var mult = 1.0;
        foreach (var m in milestones)
        {
            if (owned >= m.Threshold)
                mult *= m.CycleTimeMultiplier;
        }
        return mult;
    }

    public static double CalculateSpeedMultiplier(long owned, IReadOnlyList<SpeedMilestone>? milestones = null)
    {
        var cycleMult = CalculateCycleTimeMultiplier(owned, milestones);
        return cycleMult > 0 ? 1.0 / cycleMult : 1.0;
    }

    public static SpeedMilestone? NextSpeedMilestone(long owned, IReadOnlyList<SpeedMilestone>? milestones = null)
    {
        milestones ??= Defaults;
        foreach (var m in milestones)
        {
            if (owned < m.Threshold)
                return m;
        }
        return null;
    }
}
