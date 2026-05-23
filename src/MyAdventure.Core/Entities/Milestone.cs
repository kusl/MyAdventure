namespace MyAdventure.Core.Entities;

/// <summary>
/// A milestone threshold that grants a revenue multiplier when reached.
/// Adventure Capitalist style: owning X units of a business multiplies its revenue.
/// </summary>
public record Milestone(int Threshold, double Multiplier, string Label)
{
    /// <summary>Standard revenue milestones for all businesses.</summary>
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

/// <summary>
/// A speed milestone halves (or further reduces) a business's cycle time
/// when reached. This is the second compounding axis of mid/late-game
/// progression: revenue milestones make each cycle pay more, speed
/// milestones make cycles happen more often, and they compose
/// multiplicatively on earnings-per-second.
///
/// <para>
/// <b>Why this matters.</b> If cycle time is fixed forever, the player
/// eventually hits a wall where each new unit of business takes
/// exponentially more cash to buy but contributes the same earnings rate
/// per cycle. Buying #1001 becomes pointless because cycles still happen
/// at the same pace. Speed milestones break that wall by letting cycles
/// fire faster as ownership grows, so cash compounds rather than
/// stalls.
/// </para>
///
/// <para>
/// <b>Why this isn't aggressive.</b> The default table has only four
/// thresholds (100, 200, 300, 400), each halving cycle time. The total
/// compound speed-up is therefore capped at ×16 — meaningful, but not
/// game-breaking. By 400 owned the player already has revenue milestones
/// at 25/50/100/200/300/400 worth a combined ×64 revenue multiplier; the
/// extra ×16 speed brings earnings per second to a ×1024 boost over
/// baseline, which keeps mid-game momentum without trivializing it.
/// </para>
///
/// <para>
/// <b>Why we don't put it on the existing <see cref="Milestone"/> record.</b>
/// Keeping revenue and speed as separate axes lets each be balanced and
/// tested independently — and keeps the existing milestone tests stable
/// (no risk of accidentally regressing a ×2 revenue at 25 into a
/// "×2 revenue + ×0.5 cycle time" change). A future patch can add an
/// upgrades system that grants extra speed beyond the table without
/// having to refactor the revenue side.
/// </para>
///
/// <para>
/// <b>Robustness against very fast cycles.</b> Cycle times will get
/// small enough at the cap to be less than a single 60 Hz frame
/// (≈16 ms). The game engine's tick loop is already written to handle
/// that — it computes <c>cycles = (int)(ProgressPercent / 100.0)</c> on
/// each tick and pays out for all of them at once, with the leftover
/// fraction carried into the next tick via <c>ProgressPercent %= 100.0</c>.
/// An invariant test pins this behavior so future refactors can't break it.
/// </para>
/// </summary>
public record SpeedMilestone(int Threshold, double CycleTimeMultiplier, string Label)
{
    /// <summary>
    /// Default speed milestones — each halves cycle time. The thresholds
    /// are spread between the major revenue milestones so progression
    /// never feels stagnant for long, but the cap of 4 milestones keeps
    /// the total speed-up bounded at ×16.
    /// </summary>
    public static IReadOnlyList<SpeedMilestone> Defaults { get; } =
    [
        new(100, 0.5, "×2 Speed"),
        new(200, 0.5, "×2 Speed"),
        new(300, 0.5, "×2 Speed"),
        new(400, 0.5, "×2 Speed"),
    ];

    /// <summary>
    /// Compounded cycle-time multiplier for a given ownership count.
    /// Returns a value in (0, 1] — multiplied into base cycle time, it
    /// makes the effective cycle time shorter. At zero ownership and
    /// below the first threshold this is exactly 1.0, leaving early-game
    /// balance untouched.
    /// </summary>
    public static double CalculateCycleTimeMultiplier(int owned, IReadOnlyList<SpeedMilestone>? milestones = null)
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

    /// <summary>
    /// Convenience: cumulative speed multiplier (the reciprocal of the
    /// cycle-time multiplier). Useful for UI display ("×4 Speed").
    /// </summary>
    public static double CalculateSpeedMultiplier(int owned, IReadOnlyList<SpeedMilestone>? milestones = null)
    {
        var cycleMult = CalculateCycleTimeMultiplier(owned, milestones);
        return cycleMult > 0 ? 1.0 / cycleMult : 1.0;
    }

    /// <summary>
    /// Find the next speed milestone the player hasn't reached yet.
    /// Returns null if all speed milestones are reached.
    /// </summary>
    public static SpeedMilestone? NextSpeedMilestone(int owned, IReadOnlyList<SpeedMilestone>? milestones = null)
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
