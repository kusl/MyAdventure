using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Entities;

/// <summary>
/// Cross-business speed bonus. When EVERY business simultaneously
/// crosses a shared ownership threshold, every business gains another
/// ×2 earnings-rate multiplier. This is Option B from the AdCap-parity
/// scaling design and the second compounding axis on top of the
/// per-business speed milestones in <see cref="SpeedMilestone"/>.
///
/// <para>
/// <b>Why this exists.</b> Per-business speed milestones (Option A,
/// <see cref="SpeedMilestone.Defaults"/>) cap at ×64 — six halvings on
/// the 25/50/100/200/300/400 ladder. Without a second axis, the late
/// game asymptotes to that ceiling and progression stalls. The
/// cross-business layer adds a second ×64+ that compounds on top, and
/// — critically — keeps compounding forever: every additional +100
/// minimum-ownership boundary past 400 grants another ×2. There is no
/// terminal threshold and no asymptote. The game continues forever.
/// </para>
///
/// <para>
/// <b>Why "minimum across all businesses" is the right input.</b> The
/// AdCap rule is "all businesses simultaneously reach the same
/// threshold". The minimum ownership count across the business roster
/// IS that threshold — if the lowest-owned business has N, then by
/// definition every business has at least N. This also creates the
/// player-facing strategic incentive: balanced ownership is rewarded,
/// hoarding one business is not. A player with 1000 lemonade stands
/// and zero shrimp boats gets exactly zero cross-business bonus.
/// </para>
///
/// <para>
/// <b>Why this is a revenue multiplier, not a cycle-time divisor.</b>
/// The user's mental model is "cycle time halves again" — visually
/// identical to AdCap. Mathematically, halving cycle time and doubling
/// revenue produce the same earnings-per-second. But cycle time lives
/// in a <see cref="double"/> on <see cref="Business.CycleTimeSeconds"/>;
/// halving it 1000+ times would underflow to exactly <c>0</c> in IEEE 754
/// (below ~2^-1074), and the very next tick would divide by zero and
/// crash. Folding the entire cross-business effect into a
/// <see cref="BigDouble"/> revenue multiplier sidesteps this: revenue
/// can grow without bound because <see cref="BigDouble"/> has no
/// practical exponent ceiling. The player's earnings per second are
/// exactly what they would be under the literal-halving rule; only the
/// internal representation differs.
/// </para>
///
/// <para>
/// <b>Threshold ladder.</b> 25, 50, 100, 200, 300, 400, then every +100
/// forever (500, 600, 700, 800, ...). The first six match Option A's
/// per-business ladder one-for-one — so by the time a player has 400
/// of every business they're sitting on a ×64 per-business stack AND
/// a ×64 cross-business stack, totalling ×4096 earnings rate over
/// baseline. Past 400, the cross-business ladder continues alone but
/// never stops: at minimum-owned = 1000, the cross-business multiplier
/// is ×4096; at 10,000, it's ×2¹⁰² ≈ 5×10³⁰; and so on.
/// </para>
///
/// <para>
/// <b>Numerical robustness.</b> <see cref="CalculateSpeedMultiplier"/>
/// returns a <see cref="BigDouble"/>; the underlying <c>2^N</c>
/// computation uses <see cref="BigDouble.Pow(double)"/> which is
/// log-based and stays representable for any practically reachable N.
/// At absurd N (e.g. 10¹⁸), <see cref="BigDouble.Pow(double)"/> itself
/// saturates to <see cref="BigDouble.PositiveInfinity"/>; downstream
/// code in <see cref="MyAdventure.Core.Services.GameEngine"/> sanitizes
/// monetary values through <c>SanitizeMoney</c>, which maps
/// non-finite values to safe defaults. There is no path by which a
/// finite ownership count can produce a non-finite cash value.
/// </para>
/// </summary>
public static class CrossBusinessSpeedBonus
{
    /// <summary>
    /// How many cross-business bonus stacks the player has earned for a
    /// given minimum ownership count across all businesses. The first
    /// six thresholds match the per-business <see cref="SpeedMilestone"/>
    /// ladder (25, 50, 100, 200, 300, 400) one-for-one; past 400, one
    /// additional stack is granted per +100 owned, forever.
    /// </summary>
    /// <param name="minOwnedAcrossBusinesses">
    /// The minimum <see cref="Business.Owned"/> count across the full
    /// business roster. Negative inputs (corrupted save) are treated
    /// as zero.
    /// </param>
    /// <returns>
    /// The number of ×2 stacks granted (0 at minOwned &lt; 25; 1 at
    /// 25–49; 2 at 50–99; …; 6 at 400–499; 7 at 500–599; …). Grows
    /// linearly past 400 with one stack per +100 owned, forever.
    /// </returns>
    public static int BonusCount(int minOwnedAcrossBusinesses)
    {
        if (minOwnedAcrossBusinesses < 25) return 0;
        if (minOwnedAcrossBusinesses < 50) return 1;
        if (minOwnedAcrossBusinesses < 100) return 2;
        if (minOwnedAcrossBusinesses < 200) return 3;
        if (minOwnedAcrossBusinesses < 300) return 4;
        if (minOwnedAcrossBusinesses < 400) return 5;
        // Past 400: 6 base stacks plus one per full +100 owned.
        // Integer division floors toward zero, which is exactly what
        // we want — a player at 499 has the same 6 stacks as one at 400,
        // and only crossing into 500 grants the 7th stack.
        return 6 + (minOwnedAcrossBusinesses - 400) / 100;
    }

    /// <summary>
    /// The cross-business earnings multiplier: <c>2^BonusCount</c>,
    /// applied multiplicatively to every business's payout in both the
    /// live-tick and offline-earnings paths.
    /// <para>
    /// Returns <see cref="BigDouble.One"/> when no stacks have been
    /// earned (early game), guaranteeing exact-1.0 semantics for tests
    /// that pin "no cross-business effect below 25 owned of every
    /// business". Past that, climbs without bound.
    /// </para>
    /// </summary>
    public static BigDouble CalculateSpeedMultiplier(int minOwnedAcrossBusinesses)
    {
        var count = BonusCount(minOwnedAcrossBusinesses);
        if (count == 0) return BigDouble.One;

        // BigDouble.Pow uses a log-based formulation (10^(power × log10(this))),
        // so even huge exponents stay representable without iterative
        // multiplication. At absurdly large counts it saturates to
        // BigDouble.PositiveInfinity rather than wrapping; the engine's
        // SanitizeMoney layer catches that defensively.
        return new BigDouble(2.0).Pow(count);
    }

    /// <summary>
    /// The next minimum-ownership threshold that would grant another
    /// ×2 stack. Used by the UI to render a "next bonus at N" hint.
    /// </summary>
    /// <param name="minOwnedAcrossBusinesses">Current minimum ownership.</param>
    /// <returns>
    /// The next threshold (25, 50, 100, 200, 300, 400, then 500, 600, …).
    /// There is no "null" return — the curve continues forever.
    /// </returns>
    public static int NextThreshold(int minOwnedAcrossBusinesses)
    {
        if (minOwnedAcrossBusinesses < 25) return 25;
        if (minOwnedAcrossBusinesses < 50) return 50;
        if (minOwnedAcrossBusinesses < 100) return 100;
        if (minOwnedAcrossBusinesses < 200) return 200;
        if (minOwnedAcrossBusinesses < 300) return 300;
        if (minOwnedAcrossBusinesses < 400) return 400;
        // Past 400: next +100 boundary. Integer division then × 100
        // snaps to the next round 100. At 400 → 500; at 450 → 500;
        // at 499 → 500; at 500 → 600.
        return ((minOwnedAcrossBusinesses / 100) + 1) * 100;
    }

    /// <summary>
    /// How many more units the player needs (of the lowest-owned
    /// business) to reach the next cross-business threshold. Returned
    /// as a non-negative int; zero only when the player is exactly at
    /// a threshold boundary (in which case <see cref="NextThreshold"/>
    /// already points to the boundary after that).
    /// </summary>
    public static int UnitsToNext(int minOwnedAcrossBusinesses)
    {
        var next = NextThreshold(minOwnedAcrossBusinesses);
        var diff = next - minOwnedAcrossBusinesses;
        return diff < 0 ? 0 : diff;
    }
}
