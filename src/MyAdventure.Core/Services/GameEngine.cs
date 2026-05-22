using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Services;

/// <summary>
/// Core game engine. Processes ticks, manages businesses, handles prestige.
/// Fully testable with injected dependencies.
///
/// <para>
/// <b>BigDouble migration:</b> all monetary and progression quantities
/// (<see cref="Cash"/>, <see cref="LifetimeEarnings"/>,
/// <see cref="AngelInvestors"/>, <see cref="AngelBonus"/>) are now
/// <see cref="BigDouble"/>. The prior <c>double</c>-based implementation
/// had to clamp these values at <c>1e200</c> to avoid overflowing IEEE 754
/// to <see cref="double.PositiveInfinity"/>; that clamp is the cause of
/// the "game gets stuck at 10²⁰⁰" symptom and is GONE under <c>BigDouble</c>,
/// which has effectively no ceiling.
/// </para>
/// <para>
/// The only remaining clamp is <see cref="MaxAngelBonusExponent"/>, a
/// saturation cap on the exponential <c>1.02^N</c> bonus formula at
/// astronomical angel counts. It's set far past any practically reachable
/// value, but exists so that downstream multiplications can never produce
/// non-finite BigDoubles even under absurdly hand-edited saves.
/// </para>
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
    /// Preserving the formula keeps existing balance tests valid; the
    /// switch to BigDouble simply lifts its ceiling.
    /// </summary>
    private const double AngelMultiplierPerAngel = 1.02;

    /// <summary>
    /// Saturation cap on the AngelBonus exponent. <c>1.02^N</c> with N
    /// in the practical-game range (up to billions of angels) keeps the
    /// bonus's BigDouble exponent under ~10^7; at truly absurd angel
    /// counts (1e95+) the exponent would naturally overflow a long. We
    /// clamp it here to a finite-but-still-astronomical value so the
    /// bonus never becomes <c>BigDouble.PositiveInfinity</c> and cascade
    /// into infinite cash on a single tick. A 10^(10^15) multiplier is
    /// already a level of bonus no real player will ever notice the cap on.
    /// </summary>
    private const long MaxAngelBonusExponent = 1_000_000_000_000_000L; // 10^15

    /// <summary>
    /// Lifetime-earnings threshold below which no angels are awarded
    /// (drives the prestige unlock).
    /// </summary>
    private static readonly BigDouble PrestigeMinLifetime = new(1e12);

    /// <summary>Divisor inside <see cref="CalculateAngels"/>.</summary>
    private static readonly BigDouble AngelLifetimeDivisor = new(1e13);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public BigDouble Cash { get; private set; }
    public BigDouble LifetimeEarnings { get; private set; }
    public BigDouble AngelInvestors { get; private set; }
    public int PrestigeCount { get; private set; }
    public IReadOnlyList<Business> Businesses { get; private set; } = BusinessDefinitions.CreateDefaults();

    /// <summary>Load game state from repository.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("LoadGame");
        var state = await repository.GetLatestAsync(ct);
        if (state is null)
        {
            Cash = new BigDouble(5.0);
            logger.LogInformation("No saved game found, starting fresh with $5.00");
            return;
        }

        // Parse the canonical BigDouble strings. Sanitize defensively:
        // a corrupted save (or a hand-edited one with garbage in the
        // numeric columns) must produce a playable game, not a NaN-ridden one.
        Cash = SanitizeMoney(BigDouble.Parse(state.CashText));
        LifetimeEarnings = SanitizeMoney(BigDouble.Parse(state.LifetimeEarningsText));
        AngelInvestors = SanitizeAngels(BigDouble.Parse(state.AngelInvestorsText));
        PrestigeCount = state.PrestigeCount;

        ApplyBusinessData(state.BusinessDataJson);
        ApplyManagerData(state.ManagerDataJson);

        // Apply offline earnings via the shared public method. The same
        // calculation also serves the foreground-resume path
        // (GameViewModel.OnResumed -> ApplyOfflineEarnings). Keeping a
        // single entry point ensures cold-load and resume can never drift.
        var elapsed = _time.GetUtcNow().UtcDateTime - state.LastPlayedAt;
        var earned = ApplyOfflineEarnings(elapsed);
        if (earned.Sign > 0)
        {
            logger.LogInformation("Applied offline earnings on load: {Earnings} for {Seconds:F0}s away",
                earned.ToCanonicalString(), elapsed.TotalSeconds);
        }

        activity?.SetTag("cash", Cash.ToCanonicalString());
        activity?.SetTag("businesses_owned", Businesses.Count(b => b.Owned > 0));
    }

    /// <summary>Save current state to repository.</summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("SaveGame");
        var state = new GameState
        {
            CashText = SanitizeMoney(Cash).ToCanonicalString(),
            LifetimeEarningsText = SanitizeMoney(LifetimeEarnings).ToCanonicalString(),
            AngelInvestorsText = SanitizeAngels(AngelInvestors).ToCanonicalString(),
            PrestigeCount = PrestigeCount,
            BusinessDataJson = SerializeBusinessData(),
            ManagerDataJson = SerializeManagerData(),
            LastPlayedAt = _time.GetUtcNow().UtcDateTime
        };
        await repository.SaveAsync(state, ct);
        logger.LogDebug("Game saved. Cash: {Cash}", state.CashText);
    }

    /// <summary>Process one game tick (called ~60fps from UI timer).</summary>
    public void Tick(double deltaSeconds)
    {
        var sw = Stopwatch.StartNew();
        TickCounter.Add(1);

        // Snapshot the angel multiplier once per tick so all businesses
        // settle their cycles against the same value and so the call to
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

                // OpenTelemetry counter requires a double — saturating
                // earnings to double.MaxValue at large magnitudes is fine
                // for telemetry purposes (it's only used for graphing).
                EarningsCounter.Add(earned.ToDouble(), new KeyValuePair<string, object?>("business", biz.Id));
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
        if (cost.IsNaN || Cash < cost) return false;

        Cash = SanitizeMoney(Cash - cost);
        biz.Owned++;
        logger.LogInformation("Bought {Business} #{Count} for {Cost}", biz.Name, biz.Owned, cost.ToCanonicalString());

        // Auto-start if has manager
        if (biz.HasManager && !biz.IsRunning)
        {
            biz.IsRunning = true;
            biz.ProgressPercent = 0;
        }

        return true;
    }

    /// <summary>
    /// Buy multiple units of a business at once using the geometric-series
    /// closed-form total cost. Returns the number of units actually purchased.
    /// <para>
    /// Compared to the prior loop-based implementation:
    /// </para>
    /// <list type="bullet">
    ///   <item>O(1) regardless of <paramref name="count"/>, so "buy max" of
    ///         50,000 units takes the same time as buying 5.</item>
    ///   <item>Correctly handles very large counts that the prior 10,000-unit
    ///         safety loop would silently cap at.</item>
    ///   <item>Uses <see cref="Business.AffordableCount(BigDouble)"/> to make
    ///         "buy as many as you can afford" trivial — the buy-max path
    ///         passes <see cref="int.MaxValue"/>.</item>
    /// </list>
    /// </summary>
    public int BuyMultiple(string businessId, int count)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null || count <= 0) return 0;

        // Cap by affordability so the call can never overspend.
        var affordable = biz.AffordableCount(Cash);
        var toBuy = Math.Min(count, affordable);
        if (toBuy <= 0) return 0;

        // Cumulative geometric cost: c₀ × (rⁿ - 1) / (r - 1).
        // r and (r - 1) are doubles in the balance table; the result is BigDouble.
        var r = biz.CostMultiplier;
        BigDouble totalCost;
        if (r == 1.0)
        {
            // Degenerate: every unit costs the same.
            totalCost = biz.NextCost * toBuy;
        }
        else
        {
            var rPowN = new BigDouble(r).Pow(toBuy);
            totalCost = biz.NextCost * (rPowN - BigDouble.One) / new BigDouble(r - 1.0);
        }

        // Defensive: if the geometric-series math somehow produced a
        // larger total than cash (e.g. rounding pushed the boundary), back
        // off by one. This protects the player from a transient overdraft
        // due to floating-point noise.
        while (toBuy > 0 && Cash < totalCost)
        {
            toBuy--;
            if (toBuy == 0) { totalCost = BigDouble.Zero; break; }
            if (r == 1.0)
            {
                totalCost = biz.NextCost * toBuy;
            }
            else
            {
                var rPowN = new BigDouble(r).Pow(toBuy);
                totalCost = biz.NextCost * (rPowN - BigDouble.One) / new BigDouble(r - 1.0);
            }
        }

        if (toBuy <= 0) return 0;

        Cash = SanitizeMoney(Cash - totalCost);
        biz.Owned += toBuy;
        logger.LogInformation("Bulk bought {Count} of {Business} for {Cost} (now {Total})",
            toBuy, biz.Name, totalCost.ToCanonicalString(), biz.Owned);

        // Auto-start if has manager
        if (biz.HasManager && !biz.IsRunning)
        {
            biz.IsRunning = true;
            biz.ProgressPercent = 0;
        }

        return toBuy;
    }

    /// <summary>
    /// Buy as many units of a business as the player can afford right now.
    /// Equivalent to <see cref="BuyMultiple"/> with <see cref="int.MaxValue"/>
    /// but reads more clearly at the call site.
    /// </summary>
    public int BuyMax(string businessId) => BuyMultiple(businessId, int.MaxValue);

    /// <summary>Buy a manager for a business. Cost = 1000x base cost.</summary>
    public bool BuyManager(string businessId)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null || biz.HasManager) return false;

        var cost = new BigDouble(biz.BaseCost * 1000);
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
    public (BigDouble angels, bool success) Prestige()
    {
        var newAngels = CalculateAngels(LifetimeEarnings) - AngelInvestors;
        if (newAngels < BigDouble.One)
        {
            logger.LogInformation("Prestige rejected: not enough new angels ({Angels})",
                newAngels.ToCanonicalString());
            return (BigDouble.Zero, false);
        }

        AngelInvestors = SanitizeAngels(AngelInvestors + newAngels);
        PrestigeCount++;
        Cash = new BigDouble(5.0);
        // LifetimeEarnings is intentionally not reset — keeping it across
        // prestiges is what provides the incentive to prestige again.

        // Reset businesses
        Businesses = BusinessDefinitions.CreateDefaults();

        logger.LogInformation("Prestige #{Count}! Gained {Angels} angels",
            PrestigeCount, newAngels.ToCanonicalString());
        return (newAngels, true);
    }

    /// <summary>
    /// Compounded angel-investor revenue multiplier: <c>1.02^AngelInvestors</c>.
    /// Computed as a <see cref="BigDouble"/> via the
    /// <see cref="BigDouble.Pow(double)"/> logarithmic identity so even
    /// astronomical angel counts produce a representable result. Capped at
    /// <see cref="MaxAngelBonusExponent"/> to prevent the exponent itself
    /// from overflowing <see cref="long"/> at truly absurd angel counts —
    /// without that cap, a hand-edited save with 10^100 angels would
    /// produce <see cref="BigDouble.PositiveInfinity"/> and propagate into
    /// every monetary calculation.
    /// </summary>
    public BigDouble AngelBonus
    {
        get
        {
            if (AngelInvestors.IsNaN || AngelInvestors.Sign <= 0) return BigDouble.One;

            // Take the angel count to a double for the Pow call. The
            // exponent-as-double can lose precision at extreme counts, but
            // any precision loss happens far past where the bonus is
            // already a 1e15-magnitude multiplier — the player notices
            // nothing.
            var angelsAsDouble = AngelInvestors.ToDouble();
            if (!double.IsFinite(angelsAsDouble) || angelsAsDouble <= 0)
            {
                // Truly enormous angel count: we know the bonus is at or
                // past the cap, return the cap directly.
                return new BigDouble(1.0, MaxAngelBonusExponent, normalize: false);
            }

            var raw = new BigDouble(AngelMultiplierPerAngel).Pow(angelsAsDouble);
            if (raw.IsNaN || raw.IsInfinity || raw.Exponent > MaxAngelBonusExponent)
            {
                return new BigDouble(1.0, MaxAngelBonusExponent, normalize: false);
            }
            return raw;
        }
    }

    /// <summary>
    /// Compute how many angels the player's lifetime earnings are
    /// currently worth. Returns the cumulative count (callers subtract
    /// <see cref="AngelInvestors"/> to get the available-to-claim count).
    /// <para>
    /// Formula: <c>150 × √(LifetimeEarnings / 10¹³)</c>, floored. Returns
    /// zero below the <see cref="PrestigeMinLifetime"/> threshold (1e12).
    /// </para>
    /// </summary>
    public static BigDouble CalculateAngels(BigDouble lifetimeEarnings)
    {
        if (lifetimeEarnings.IsNaN || lifetimeEarnings.Sign < 0) return BigDouble.Zero;
        if (lifetimeEarnings.IsInfinity)
        {
            // Defensive: an infinite lifetime is a corrupted-save signal.
            // Return zero rather than propagating infinity into angel count.
            return BigDouble.Zero;
        }
        if (lifetimeEarnings < PrestigeMinLifetime) return BigDouble.Zero;

        var raw = new BigDouble(150.0) * (lifetimeEarnings / AngelLifetimeDivisor).Sqrt();
        return raw.Floor();
    }

    /// <summary>
    /// Apply offline earnings for a given elapsed time span and return how
    /// much was earned. This is the single public entry point used by both
    /// <see cref="LoadAsync"/> (cold start) and the foreground-resume path
    /// on the ViewModel layer.
    /// </summary>
    public BigDouble ApplyOfflineEarnings(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= MinimumOfflineGapSeconds) return BigDouble.Zero;

        var earned = CalculateOfflineEarnings(elapsed);
        if (earned.Sign <= 0) return BigDouble.Zero;

        Cash = SanitizeMoney(Cash + earned);
        LifetimeEarnings = SanitizeMoney(LifetimeEarnings + earned);

        EarningsCounter.Add(earned.ToDouble(), new KeyValuePair<string, object?>("source", "offline"));
        return earned;
    }

    private BigDouble CalculateOfflineEarnings(TimeSpan elapsed)
    {
        var total = BigDouble.Zero;
        foreach (var biz in Businesses.Where(b => b.HasManager && b.Owned > 0))
        {
            var cycles = new BigDouble(elapsed.TotalSeconds / biz.CycleTimeSeconds);
            total += biz.Revenue * cycles;
        }
        // AngelBonus applied once at the end, matching Tick's per-cycle path
        // (which multiplies the per-tick earned amount by the snapshot bonus).
        return total * AngelBonus;
    }

    /// <summary>
    /// Sanitize a monetary value. NaN → 0. Negative → 0. The
    /// double-precision-era <c>1e200</c> ceiling is GONE under BigDouble.
    /// Infinity is mapped to zero defensively (it shouldn't appear except
    /// from a corrupted save — clamping rather than propagating is the
    /// only safe choice).
    /// </summary>
    private static BigDouble SanitizeMoney(BigDouble value)
    {
        if (value.IsNaN) return BigDouble.Zero;
        if (value.IsInfinity) return BigDouble.Zero;
        if (value.Sign < 0) return BigDouble.Zero;
        return value;
    }

    /// <summary>
    /// Sanitize an angel count. No ceiling under BigDouble — the
    /// AngelBonus computation has its own saturation cap that protects
    /// downstream arithmetic regardless of angel count.
    /// </summary>
    private static BigDouble SanitizeAngels(BigDouble value)
    {
        if (value.IsNaN || value.IsInfinity || value.Sign < 0) return BigDouble.Zero;
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
    /// Numeric values are serialized as canonical BigDouble strings
    /// (e.g. <c>"1.5e200"</c>) so even astronomical amounts round-trip
    /// without precision loss. This is also what <see cref="GameState"/>
    /// stores in SQLite, so an exported string is essentially a portable
    /// dump of the persisted columns.
    /// </para>
    /// </summary>
    public string ExportToString()
    {
        var data = new Dictionary<string, object>
        {
            ["v"] = 2, // bumped from 1 to signal BigDouble-string format
            ["cash"] = SanitizeMoney(Cash).ToCanonicalString(),
            ["lifetime"] = SanitizeMoney(LifetimeEarnings).ToCanonicalString(),
            ["angels"] = SanitizeAngels(AngelInvestors).ToCanonicalString(),
            ["prestige"] = PrestigeCount,
            ["businesses"] = Businesses.ToDictionary(b => b.Id, b => b.Owned),
            ["managers"] = Businesses.ToDictionary(b => b.Id, b => b.HasManager)
        };
        var json = JsonSerializer.Serialize(data);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Import game state from a Base64-encoded JSON string. Accepts both
    /// the current v2 format (numbers as canonical BigDouble strings) and
    /// the legacy v1 format (numbers as native JSON doubles), so old
    /// saves from before the BigDouble migration still load.
    /// </summary>
    public bool ImportFromString(string encoded)
    {
        try
        {
            var json = Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded.Trim()));
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (data is null) return false;

            Cash = SanitizeMoney(ReadBigDouble(data, "cash"));
            LifetimeEarnings = SanitizeMoney(ReadBigDouble(data, "lifetime"));
            AngelInvestors = SanitizeAngels(ReadBigDouble(data, "angels"));
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

            logger.LogInformation("Imported game state. Cash: {Cash}, Angels: {Angels}",
                Cash.ToCanonicalString(), AngelInvestors.ToCanonicalString());
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import game state");
            return false;
        }
    }

    /// <summary>
    /// Read a numeric field from a JSON dictionary as a <see cref="BigDouble"/>,
    /// transparently handling both string (v2 format) and number (legacy v1)
    /// representations.
    /// </summary>
    private static BigDouble ReadBigDouble(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var el)) return BigDouble.Zero;

        return el.ValueKind switch
        {
            JsonValueKind.String => BigDouble.TryParse(el.GetString(), out var parsed) ? parsed : BigDouble.Zero,
            JsonValueKind.Number => new BigDouble(el.GetDouble()),
            _ => BigDouble.Zero,
        };
    }
}
