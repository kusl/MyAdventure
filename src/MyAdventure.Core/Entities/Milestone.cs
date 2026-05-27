namespace MyAdventure.Core.Entities;

/// <summary>
/// A milestone threshold that grants a revenue multiplier when reached.
/// Adventure Capitalist style: owning X units of a business multiplies its revenue.
/// <para>
/// <b>Owned-parameter widening:</b> all milestone helpers accept
/// <see cref="long"/> for the owned count to match
/// <see cref="Business.Owned"/>'s widened type. The thresholds themselves
/// stay <see cref="int"/> — they are small, bounded constants — but the
/// caller-supplied count can be arbitrarily large. <c>int</c>-typed
/// arguments at call sites widen implicitly, so the existing test suite
/// continues to compile without edits.
/// </para>
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

    /// <summary>
    /// Find the next milestone the player hasn't reached yet.
    /// Returns null if all milestones are reached.
    /// </summary>
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

    /// <summary>
    /// How many more units needed to reach the next milestone. Returns 0
    /// when no milestones remain. Returns a non-negative value even when
    /// <paramref name="owned"/> happens to land exactly on a threshold
    /// (in which case the "next" milestone is the one AFTER that).
    /// </summary>
    public static long UnitsToNext(long owned, IReadOnlyList<Milestone>? milestones = null)
    {
        var next = NextMilestone(owned, milestones);
        return next is null ? 0L : next.Threshold - owned;
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
/// <b>AdCap-parity ladder (updated from the original 4-threshold table).</b>
/// The default table now has SIX thresholds — 25, 50, 100, 200, 300, 400
/// — matching the per-business speed table in Adventure Capitalist
/// exactly. Each halves cycle time, so the compound speed-up caps at
/// ×64 per business (six halvings: 2⁶ = 64). The original table only
/// had four (100/200/300/400 → ×16) and that was the user's reported
/// gap from AdCap; this brings Option A to parity.
/// </para>
///
/// <para>
/// <b>The cap is intentional — for THIS axis.</b> Per-business speed
/// stays at ×64 max because cycle time is stored in a <see cref="double"/>
/// (<see cref="Business.CycleTimeSeconds"/>) and further halvings would
/// quickly underflow when combined with a small base time. The
/// second-axis cross-business bonus (<see cref="CrossBusinessSpeedBonus"/>)
/// is what scales without bound — it's a <see cref="MyAdventure.Core.Numerics.BigDouble"/>
/// revenue multiplier and has no ceiling.
/// </para>
///
/// <para>
/// <b>Robustness against very fast cycles.</b> At 400 owned of a 0.6 s
/// base business (lemonade), the effective cycle drops to 9.375 ms —
/// below a single 60 Hz frame (16 ms). The game engine's tick loop is
/// already written to handle that: it computes
/// <c>cycles = (int)(ProgressPercent / 100.0)</c> on each tick and pays
/// out for all of them at once, with the leftover fraction carried into
/// the next tick via <c>ProgressPercent %= 100.0</c>. The
/// <c>SubFrameCycleTests</c> invariants pin this behavior so future
/// refactors can't break it.
/// </para>
///
/// <para>
/// <b>Owned-parameter widening (matching <see cref="Milestone"/>):</b>
/// all speed-milestone helpers accept <see cref="long"/>. The defaults'
/// <see cref="Threshold"/> field stays <see cref="int"/> since the table
/// is bounded; only the caller-supplied owned count widens.
/// </para>
/// </summary>
public record SpeedMilestone(int Threshold, double CycleTimeMultiplier, string Label)
{
    /// <summary>
    /// Default speed milestones — six thresholds matching the AdCap
    /// per-business speed ladder (25, 50, 100, 200, 300, 400). Each
    /// halves cycle time; compounded, they reach ×64 max per business.
    /// The cross-business bonus in <see cref="CrossBusinessSpeedBonus"/>
    /// stacks multiplicatively on top of this and is uncapped.
    /// </summary>
    public static IReadOnlyList<SpeedMilestone> Defaults { get; } =
    [
        new(25, 0.5, "×2 Speed"),
        new(50, 0.5, "×2 Speed"),
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

    /// <summary>
    /// Convenience: cumulative speed multiplier (the reciprocal of the
    /// cycle-time multiplier). Useful for UI display ("×64 Speed").
    /// </summary>
    public static double CalculateSpeedMultiplier(long owned, IReadOnlyList<SpeedMilestone>? milestones = null)
    {
        var cycleMult = CalculateCycleTimeMultiplier(owned, milestones);
        return cycleMult > 0 ? 1.0 / cycleMult : 1.0;
    }

    /// <summary>
    /// Find the next speed milestone the player hasn't reached yet.
    /// Returns null if all speed milestones are reached.
    /// </summary>
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
