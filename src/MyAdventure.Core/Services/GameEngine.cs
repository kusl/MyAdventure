using System.Text;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;

namespace MyAdventure.Core.Services;

/// <summary>
/// Core game engine. Processes ticks, manages businesses, handles prestige.
/// Fully testable with injected dependencies.
/// </summary>
public class GameEngine(
    IGameStateRepository repository,
    ILogger<GameEngine> logger,
    TimeProvider? timeProvider = null)
{
    private static readonly ActivitySource ActivitySource = new("MyAdventure.GameEngine");
    private static readonly Meter GameMeter = new("MyAdventure.Game");
    private static readonly Counter<long> TickCounter = GameMeter.CreateCounter<long>("game.ticks");
    private static readonly Counter<double> EarningsCounter = GameMeter.CreateCounter<double>("game.earnings");
    private static readonly Histogram<double> TickDuration = GameMeter.CreateHistogram<double>("game.tick_duration_ms");

    /// <summary>
    /// Threshold below which an "offline" gap is treated as no gap at all.
    /// One second is comfortably above any normal tick delta (~16 ms) and
    /// well below any user-perceptible pause, so it can't accidentally
    /// double-count live ticks while still firing for any real suspension.
    /// </summary>
    private const double MinimumOfflineGapSeconds = 1.0;

    /// <summary>
    /// Per-angel revenue multiplier base. Each angel multiplies revenue by
    /// this value, compounded — so 50 angels = 1.02^50 ≈ ×2.69, not ×2.00.
    /// <para>
    /// The compound formulation is what restores prestige as a meaningful
    /// progression mechanic deep into the game: under the previous linear
    /// "+2% per angel" formula a player with 700+ angels was getting only
    /// a ×15 multiplier, and one more prestige (gaining ~140 angels)
    /// would only edge that up to ×17 — not enough to motivate a reset.
    /// Compounded, the same prestige goes from ×4.4M to ×68M, which is
    /// the "press the button" moment idle games are built around.
    /// </para>
    /// </summary>
    private const double AngelMultiplierPerAngel = 1.02;

    /// <summary>
    /// Hard cap on <see cref="AngelBonus"/>. <c>1.02^N</c> overflows IEEE 754
    /// doubles around N ≈ 35,750 angels, producing <see cref="double.PositiveInfinity"/>.
    /// Once any multiplier is Infinity, every downstream calculation
    /// (cash, lifetime earnings, percent display, JSON export) either
    /// becomes Infinity, NaN, or throws — the exact failure mode that
    /// produced "infinity D infinity angels + infinity D% Next +NaN" in
    /// the wild. Capping the bonus keeps every downstream value finite
    /// without taking anything away from the player: a ×10⁹⁰ multiplier
    /// is still effectively unbounded for game purposes.
    /// </summary>
    private const double MaxAngelBonus = 1e90;

    /// <summary>
    /// Hard cap on any monetary quantity (<see cref="Cash"/>,
    /// <see cref="LifetimeEarnings"/>) to keep arithmetic finite. Chosen
    /// well below <see cref="double.MaxValue"/> (~1.8e308) so even a few
    /// further multiplications can't push the value to Infinity.
    /// </summary>
    private const double MaxMoney = 1e200;

    /// <summary>
    /// Hard cap on <see cref="AngelInvestors"/>. Past this point, the
    /// <see cref="AngelBonus"/> is already at <see cref="MaxAngelBonus"/>,
    /// so further angels have no in-game effect anyway. Capping keeps
    /// the stored value sensible and round-trippable through saves and
    /// exports.
    /// </summary>
    private const double MaxAngelInvestors = 1e9;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public double Cash { get; private set; }
    public double LifetimeEarnings { get; private set; }
    public double AngelInvestors { get; private set; }
    public int PrestigeCount { get; private set; }
    public IReadOnlyList<Business> Businesses { get; private set; } = BusinessDefinitions.CreateDefaults();

    /// <summary>Load game state from repository.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("LoadGame");
        var state = await repository.GetLatestAsync(ct);
        if (state is null)
        {
            Cash = 5.0;
            logger.LogInformation("No saved game found, starting fresh with ${Cash:F2}", Cash);
            return;
        }

        // Sanitize anything loaded from disk. Older saves predating the
        // overflow fix may contain Infinity / NaN serialized as raw
        // doubles in SQLite. Loading those values unchecked would
        // immediately re-corrupt the in-memory state. The clamp also
        // protects against hand-edited save files (which we explicitly
        // permit) that set Cash or AngelInvestors to a wildly large value.
        Cash = SanitizeMoney(state.Cash);
        LifetimeEarnings = SanitizeMoney(state.LifetimeEarnings);
        AngelInvestors = SanitizeAngels(state.AngelInvestors);
        PrestigeCount = state.PrestigeCount;

        ApplyBusinessData(state.BusinessDataJson);
        ApplyManagerData(state.ManagerDataJson);

        // Apply offline earnings via the shared public method. The same
        // calculation now also serves the foreground-resume path
        // (GameViewModel.OnResumed -> ApplyOfflineEarnings). Keeping a
        // single entry point ensures cold-load and resume can never drift.
        var elapsed = _time.GetUtcNow().UtcDateTime - state.LastPlayedAt;
        var earned = ApplyOfflineEarnings(elapsed);
        if (earned > 0)
        {
            logger.LogInformation("Applied offline earnings on load: {Earnings:F2} for {Seconds:F0}s away",
                earned, elapsed.TotalSeconds);
        }

        activity?.SetTag("cash", Cash);
        activity?.SetTag("businesses_owned", Businesses.Count(b => b.Owned > 0));
    }

    /// <summary>Save current state to repository.</summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("SaveGame");
        var state = new GameState
        {
            Cash = SanitizeMoney(Cash),
            LifetimeEarnings = SanitizeMoney(LifetimeEarnings),
            AngelInvestors = SanitizeAngels(AngelInvestors),
            PrestigeCount = PrestigeCount,
            BusinessDataJson = SerializeBusinessData(),
            ManagerDataJson = SerializeManagerData(),
            LastPlayedAt = _time.GetUtcNow().UtcDateTime
        };
        await repository.SaveAsync(state, ct);
        logger.LogDebug("Game saved. Cash: {Cash:F2}", state.Cash);
    }

    /// <summary>Process one game tick (called ~60fps from UI timer).</summary>
    public void Tick(double deltaSeconds)
    {
        var sw = Stopwatch.StartNew();
        TickCounter.Add(1);

        // Snapshot the angel multiplier once per tick so all businesses
        // settle their cycles against the same value, and so the call to
        // AngelBonus is paid once instead of per-business.
        var angelBonus = AngelBonus;

        foreach (var biz in Businesses)
        {
            if (!biz.IsRunning || biz.Owned <= 0) continue;

            biz.ProgressPercent += (deltaSeconds / biz.CycleTimeSeconds) * 100.0;

            if (biz.ProgressPercent >= 100.0)
            {
                var cycles = (int)(biz.ProgressPercent / 100.0);
                // Angel bonus applies to live earnings just like it does to
                // offline earnings — see CalculateOfflineEarnings(), which
                // multiplies by AngelBonus once at the end. These two paths
                // must stay in sync; an invariant test guards this.
                var earned = biz.Revenue * cycles * angelBonus;
                Cash = SanitizeMoney(Cash + earned);
                LifetimeEarnings = SanitizeMoney(LifetimeEarnings + earned);
                EarningsCounter.Add(earned, new KeyValuePair<string, object?>("business", biz.Id));
                biz.ProgressPercent %= 100.0;

                // Auto-restart if has manager
                if (!biz.HasManager)
                    biz.IsRunning = false;
            }
        }

        sw.Stop();
        TickDuration.Record(sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Player clicks a business to start its cycle.</summary>
    public bool StartBusiness(string businessId)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null || biz.Owned <= 0 || biz.IsRunning) return false;

        biz.IsRunning = true;
        biz.ProgressPercent = 0;
        logger.LogDebug("Started business: {Business}", biz.Name);
        return true;
    }

    /// <summary>Buy one unit of a business.</summary>
    public bool BuyBusiness(string businessId)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null) return false;

        var cost = biz.NextCost;
        // If cost itself has gone non-finite due to extreme ownership counts,
        // refuse the buy rather than corrupting Cash via subtraction.
        if (!double.IsFinite(cost) || Cash < cost) return false;

        Cash = SanitizeMoney(Cash - cost);
        biz.Owned++;
        logger.LogInformation("Bought {Business} #{Count} for {Cost:F2}", biz.Name, biz.Owned, cost);

        // Auto-start if has manager
        if (biz.HasManager && !biz.IsRunning)
        {
            biz.IsRunning = true;
            biz.ProgressPercent = 0;
        }

        return true;
    }

    /// <summary>
    /// Buy multiple units of a business at once.
    /// Returns the number of units actually purchased.
    /// </summary>
    public int BuyMultiple(string businessId, int count)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null || count <= 0) return 0;

        var bought = 0;
        for (var i = 0; i < count; i++)
        {
            var cost = biz.NextCost;
            if (!double.IsFinite(cost) || Cash < cost) break;
            Cash = SanitizeMoney(Cash - cost);
            biz.Owned++;
            bought++;
        }

        if (bought > 0)
        {
            logger.LogInformation("Bulk bought {Count} of {Business} (now {Total})", bought, biz.Name, biz.Owned);

            // Auto-start if has manager
            if (biz.HasManager && !biz.IsRunning)
            {
                biz.IsRunning = true;
                biz.ProgressPercent = 0;
            }
        }

        return bought;
    }

    /// <summary>Buy a manager for a business. Cost = 1000x base cost.</summary>
    public bool BuyManager(string businessId)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null || biz.HasManager) return false;

        var cost = biz.BaseCost * 1000;
        if (Cash < cost) return false;

        Cash = SanitizeMoney(Cash - cost);
        biz.HasManager = true;

        if (biz.Owned > 0 && !biz.IsRunning)
        {
            biz.IsRunning = true;
            biz.ProgressPercent = 0;
        }

        logger.LogInformation("Bought manager for {Business}", biz.Name);
        return true;
    }

    /// <summary>Prestige: reset businesses, gain angel investors.</summary>
    public (double angels, bool success) Prestige()
    {
        var newAngels = CalculateAngels(LifetimeEarnings) - AngelInvestors;
        if (newAngels < 1)
        {
            logger.LogInformation("Prestige rejected: not enough new angels ({Angels:F2})", newAngels);
            return (0, false);
        }

        AngelInvestors = SanitizeAngels(AngelInvestors + newAngels);
        PrestigeCount++;
        Cash = 5.0;
        // LifetimeEarnings is intentionally not reset — keeping it across
        // prestiges is what provides the incentive to prestige again.

        // Reset businesses
        var defaults = BusinessDefinitions.CreateDefaults();
        Businesses = defaults;

        logger.LogInformation("Prestige #{Count}! Gained {Angels:F0} angels", PrestigeCount, newAngels);
        return (newAngels, true);
    }

    /// <summary>
    /// Compounded angel-investor revenue multiplier:
    /// <c>min(1.02 ^ AngelInvestors, MaxAngelBonus)</c>. Every angel adds
    /// 2% on top of the previous angel's contribution rather than 2% of
    /// base revenue, capped at <see cref="MaxAngelBonus"/> to prevent
    /// IEEE 754 overflow at ~35,750 angels.
    /// <para>
    /// 50 angels = ×2.69. 200 angels = ×52.5. 1000 angels = ×4.0×10^8.
    /// At the cap, the bonus is still <c>1e90</c> — effectively unbounded
    /// for game purposes — but it can never become <see cref="double.PositiveInfinity"/>,
    /// which is what allows every downstream multiplication, percentage
    /// computation, and JSON export to remain finite and safe.
    /// </para>
    /// <para>
    /// Save compatibility: the formula is computed from
    /// <see cref="AngelInvestors"/>, which is unchanged on disk. Old
    /// saves load and immediately benefit from the compound multiplier
    /// without any migration step; saves that happen to contain a wildly
    /// large angel count are clamped on load by <see cref="SanitizeAngels"/>.
    /// </para>
    /// </summary>
    public double AngelBonus
    {
        get
        {
            if (!double.IsFinite(AngelInvestors) || AngelInvestors <= 0) return 1.0;
            var raw = Math.Pow(AngelMultiplierPerAngel, AngelInvestors);
            if (!double.IsFinite(raw) || raw > MaxAngelBonus) return MaxAngelBonus;
            return raw;
        }
    }

    /// <summary>
    /// Compute how many angels the player's lifetime earnings are
    /// currently worth. Returns the cumulative count, not the delta —
    /// callers subtract <see cref="AngelInvestors"/> to get the
    /// available-to-claim count.
    /// <para>
    /// Defensive against non-finite or absurdly large inputs: returns 0
    /// for NaN/negative input and clamps the result so that
    /// <see cref="Math.Sqrt"/> of a finite-but-huge input doesn't propagate
    /// through to <see cref="AngelInvestors"/> as Infinity.
    /// </para>
    /// </summary>
    public static double CalculateAngels(double lifetimeEarnings)
    {
        if (double.IsNaN(lifetimeEarnings) || lifetimeEarnings < 1e12) return 0;
        if (!double.IsFinite(lifetimeEarnings)) return MaxAngelInvestors;
        var raw = Math.Floor(150 * Math.Sqrt(lifetimeEarnings / 1e13));
        if (!double.IsFinite(raw)) return MaxAngelInvestors;
        return Math.Min(raw, MaxAngelInvestors);
    }

    /// <summary>
    /// Apply offline earnings for a given elapsed time span and return
    /// how much was earned. This is the single public entry point used by
    /// both <see cref="LoadAsync"/> (cold start) and the foreground-resume
    /// path on the ViewModel layer. Keeping a single calculation reachable
    /// from both call sites is what allows live ticks and offline payouts
    /// to remain provably equivalent.
    ///
    /// <para>
    /// Behavior:
    /// <list type="bullet">
    ///   <item>Returns 0 if <paramref name="elapsed"/> is at or below the
    ///         <see cref="MinimumOfflineGapSeconds"/> threshold — protects
    ///         against being called with a near-zero or negative span,
    ///         which would otherwise generate a tiny double-count next to
    ///         the live tick loop.</item>
    ///   <item>Returns 0 if no business currently has a manager and at
    ///         least one unit owned. Manager-less businesses require an
    ///         active player click to run — the offline path deliberately
    ///         excludes them.</item>
    ///   <item>Adds the result to both <see cref="Cash"/> and
    ///         <see cref="LifetimeEarnings"/>. Lifetime earnings drives
    ///         prestige progression, so offline earnings must count there
    ///         identically to live earnings.</item>
    ///   <item>The <see cref="AngelBonus"/> is applied <i>once</i> at the
    ///         end of the calculation — never per-cycle inside the loop —
    ///         to mirror the live <see cref="Tick"/> path. Drift between
    ///         the two is guarded by an invariant test.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="elapsed">Wall-clock duration the player was away.</param>
    /// <returns>The amount earned (already added to Cash and LifetimeEarnings).</returns>
    public double ApplyOfflineEarnings(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= MinimumOfflineGapSeconds) return 0;

        var earned = CalculateOfflineEarnings(elapsed);
        if (earned <= 0) return 0;

        Cash = SanitizeMoney(Cash + earned);
        LifetimeEarnings = SanitizeMoney(LifetimeEarnings + earned);

        EarningsCounter.Add(earned, new KeyValuePair<string, object?>("source", "offline"));
        return earned;
    }

    private double CalculateOfflineEarnings(TimeSpan elapsed)
    {
        double total = 0;
        foreach (var biz in Businesses.Where(b => b.HasManager && b.Owned > 0))
        {
            var cycles = elapsed.TotalSeconds / biz.CycleTimeSeconds;
            total += biz.Revenue * cycles;
        }
        // AngelBonus is applied once here at the end of the offline path,
        // matching the per-cycle application inside Tick(). Do not multiply
        // biz.Revenue by AngelBonus inside the loop — that would
        // double-apply when paired with this final multiplication.
        return total * AngelBonus;
    }

    /// <summary>
    /// Clamp a monetary value to a finite, sensible range. NaN becomes 0;
    /// Infinity becomes <see cref="MaxMoney"/>; negative becomes 0.
    /// </summary>
    private static double SanitizeMoney(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value < 0) return 0;
        if (value > MaxMoney) return MaxMoney;
        return value;
    }

    /// <summary>
    /// Clamp an angel count to a finite, sensible range. See <see cref="MaxAngelInvestors"/>.
    /// </summary>
    private static double SanitizeAngels(double value)
    {
        if (double.IsNaN(value) || value < 0) return 0;
        if (value > MaxAngelInvestors) return MaxAngelInvestors;
        return value;
    }

    private void ApplyBusinessData(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
            foreach (var biz in Businesses)
                if (data.TryGetValue(biz.Id, out var owned))
                    biz.Owned = Math.Max(0, owned);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse business data");
        }
    }

    private void ApplyManagerData(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? [];
            foreach (var biz in Businesses)
                if (data.TryGetValue(biz.Id, out var has))
                {
                    biz.HasManager = has;
                    if (has && biz.Owned > 0)
                    {
                        biz.IsRunning = true;
                    }
                }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse manager data");
        }
    }

    private string SerializeBusinessData() =>
        JsonSerializer.Serialize(Businesses.ToDictionary(b => b.Id, b => b.Owned));

    private string SerializeManagerData() =>
        JsonSerializer.Serialize(Businesses.ToDictionary(b => b.Id, b => b.HasManager));


    /// <summary>
    /// Export full game state as a Base64-encoded JSON string.
    /// Players can freely edit the decoded JSON — we encourage tinkering.
    /// <para>
    /// All numeric values are sanitized through <see cref="SanitizeMoney"/>
    /// / <see cref="SanitizeAngels"/> before serialization. This is the
    /// final defense against any non-finite value ever leaving the
    /// process: <see cref="JsonSerializer"/> with default options throws
    /// <see cref="ArgumentException"/> on Infinity/NaN, which previously
    /// force-closed the app when the user pressed Export. The engine's
    /// own state is already clamped on load and after every tick, but
    /// running the sanitizer one more time on the way out makes Export
    /// safe even if some upstream invariant gets violated.
    /// </para>
    /// </summary>
    public string ExportToString()
    {
        var data = new Dictionary<string, object>
        {
            ["v"] = 1,
            ["cash"] = SanitizeMoney(Cash),
            ["lifetime"] = SanitizeMoney(LifetimeEarnings),
            ["angels"] = SanitizeAngels(AngelInvestors),
            ["prestige"] = PrestigeCount,
            ["businesses"] = Businesses.ToDictionary(b => b.Id, b => b.Owned),
            ["managers"] = Businesses.ToDictionary(b => b.Id, b => b.HasManager)
        };
        var json = JsonSerializer.Serialize(data);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Import game state from a Base64-encoded JSON string.
    /// Returns true on success, false if the string is invalid.
    /// </summary>
    public bool ImportFromString(string encoded)
    {
        try
        {
            var json = Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded.Trim()));
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (data is null) return false;

            // Read raw values, then sanitize. A hand-edited save that
            // pokes Infinity into the JSON would otherwise propagate
            // straight into engine state and resurrect the original bug.
            Cash = SanitizeMoney(data.TryGetValue("cash", out var cashEl) ? cashEl.GetDouble() : 0);
            LifetimeEarnings = SanitizeMoney(data.TryGetValue("lifetime", out var ltEl) ? ltEl.GetDouble() : 0);
            AngelInvestors = SanitizeAngels(data.TryGetValue("angels", out var angEl) ? angEl.GetDouble() : 0);
            PrestigeCount = data.TryGetValue("prestige", out var prEl) ? prEl.GetInt32() : 0;

            Businesses = BusinessDefinitions.CreateDefaults();

            if (data.TryGetValue("businesses", out var bizEl))
            {
                var bizData = JsonSerializer.Deserialize<Dictionary<string, int>>(bizEl.GetRawText()) ?? [];
                foreach (var biz in Businesses)
                    if (bizData.TryGetValue(biz.Id, out var owned))
                        biz.Owned = Math.Max(0, owned);
            }

            if (data.TryGetValue("managers", out var mgrEl))
            {
                var mgrData = JsonSerializer.Deserialize<Dictionary<string, bool>>(mgrEl.GetRawText()) ?? [];
                foreach (var biz in Businesses)
                    if (mgrData.TryGetValue(biz.Id, out var has))
                    {
                        biz.HasManager = has;
                        if (has && biz.Owned > 0) biz.IsRunning = true;
                    }
            }

            logger.LogInformation("Imported game state. Cash: {Cash:F2}, Angels: {Angels:F0}", Cash, AngelInvestors);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import game state");
            return false;
        }
    }
}
