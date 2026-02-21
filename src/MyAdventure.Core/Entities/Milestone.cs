namespace MyAdventure.Core.Entities;

/// <summary>
/// A milestone threshold that grants a revenue multiplier when reached.
/// Adventure Capitalist style: owning X units of a business multiplies its revenue.
/// </summary>
public record Milestone(int Threshold, double Multiplier, string Label)
{
    /// <summary>Standard milestones for all businesses.</summary>
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

    /// <summary>
    /// Calculate the combined multiplier for a given ownership count.
    /// Each milestone compounds multiplicatively.
    /// </summary>
    public static double CalculateMultiplier(int owned, IReadOnlyList<Milestone>? milestones = null)
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

    /// <summary>
    /// Find the next milestone the player hasn't reached yet.
    /// Returns null if all milestones are reached.
    /// </summary>
    public static Milestone? NextMilestone(int owned, IReadOnlyList<Milestone>? milestones = null)
    {
        milestones ??= Defaults;
        foreach (var m in milestones)
        {
            if (owned < m.Threshold)
                return m;
        }
        return null;
    }

    /// <summary>How many more units needed to reach the next milestone.</summary>
    public static int UnitsToNext(int owned, IReadOnlyList<Milestone>? milestones = null)
    {
        var next = NextMilestone(owned, milestones);
        return next is null ? 0 : next.Threshold - owned;
    }
}
