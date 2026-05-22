namespace MyAdventure.Core.Entities;

/// <summary>
/// Persistent game state stored in SQLite.
///
/// <para>
/// All monetary and counter values that can grow unboundedly in an idle
/// game are stored as <see cref="string"/> in the canonical
/// <c>BigDouble</c> form (e.g. <c>"1.5e200"</c>). The string-based storage
/// is what lets the game progress past the <see cref="double"/> ceiling
/// of ~1.8 × 10³⁰⁸: a 64-bit double simply cannot represent the values an
/// active player accumulates after a few prestige cycles, and any
/// numeric column type would either clip or lose precision.
/// </para>
///
/// <para>
/// The conversion between <c>BigDouble</c> in memory and string on disk
/// is performed by the EF Core <c>ValueConverter</c> registered in
/// <c>AppDbContext.OnModelCreating</c>. <see cref="GameEngine"/> works
/// exclusively in <c>BigDouble</c> and is unaware of the string form.
/// </para>
/// </summary>
public record GameState : EntityBase
{
    /// <summary>Current cash, canonical BigDouble string.</summary>
    public string CashText { get; set; } = "0";

    /// <summary>Lifetime earnings (drives prestige threshold), canonical BigDouble string.</summary>
    public string LifetimeEarningsText { get; set; } = "0";

    /// <summary>Angel investor count, canonical BigDouble string.</summary>
    public string AngelInvestorsText { get; set; } = "0";

    /// <summary>How many times the player has prestiged.</summary>
    public int PrestigeCount { get; set; }

    /// <summary>JSON dictionary of business-id → owned-count.</summary>
    public string BusinessDataJson { get; set; } = "{}";

    /// <summary>JSON dictionary of business-id → has-manager.</summary>
    public string ManagerDataJson { get; set; } = "{}";

    /// <summary>
    /// Wall-clock timestamp the player was last active. Used by the
    /// offline-earnings calculation on resume / cold-load.
    /// </summary>
    public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
}
