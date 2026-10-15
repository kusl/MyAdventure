using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Services;

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

    private const double MinimumOfflineGapSeconds = 1.0;

    private static readonly BigDouble PrestigeMinLifetime = new(GameBalance.PrestigeMinLifetime);
    private static readonly BigDouble AngelLifetimeDivisor = new(GameBalance.AngelLifetimeDivisor);
    private static readonly BigDouble AngelScale = new(GameBalance.AngelScale);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public BigDouble Cash { get; private set; }
    public BigDouble LifetimeEarnings { get; private set; }
    public BigDouble AngelInvestors { get; private set; }
    public int PrestigeCount { get; private set; }
    public IReadOnlyList<Business> Businesses { get; private set; } = BusinessDefinitions.CreateDefaults();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("LoadGame");
        var state = await repository.GetLatestAsync(ct);
        if (state is null)
        {
            Cash = new BigDouble(GameBalance.StartingCash);
            logger.LogInformation("No saved game found, starting fresh with {Cash}", GameBalance.StartingCash);
            return;
        }

        Cash = SanitizeMoney(BigDouble.Parse(state.CashText));
        LifetimeEarnings = SanitizeMoney(BigDouble.Parse(state.LifetimeEarningsText));
        AngelInvestors = SanitizeAngels(BigDouble.Parse(state.AngelInvestorsText));
        PrestigeCount = state.PrestigeCount;

        ApplyBusinessData(state.BusinessDataJson);
        ApplyManagerData(state.ManagerDataJson);

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

    public void Tick(double deltaSeconds)
    {
        var sw = Stopwatch.StartNew();
        TickCounter.Add(1);

        var totalBonus = AngelBonus * CrossBusinessSpeedMultiplier;

        foreach (var biz in Businesses)
        {
            if (!biz.IsRunning || biz.Owned <= 0) continue;

            biz.ProgressPercent += deltaSeconds / biz.CycleTimeSeconds * 100.0;
            if (biz.ProgressPercent < 100.0) continue;

            var cycles = Math.Floor(biz.ProgressPercent / 100.0);
            var earned = biz.Revenue * new BigDouble(cycles) * totalBonus;
            Cash = SanitizeMoney(Cash + earned);
            LifetimeEarnings = SanitizeMoney(LifetimeEarnings + earned);
            EarningsCounter.Add(earned.ToDouble(), new KeyValuePair<string, object?>("business", biz.Id));
            biz.ProgressPercent %= 100.0;

            if (!biz.HasManager)
                biz.IsRunning = false;
        }

        sw.Stop();
        TickDuration.Record(sw.Elapsed.TotalMilliseconds);
    }

    public bool StartBusiness(string businessId)
    {
        var biz = Find(businessId);
        if (biz is null || biz.Owned <= 0 || biz.IsRunning) return false;

        biz.IsRunning = true;
        biz.ProgressPercent = 0;
        logger.LogDebug("Started business: {Business}", biz.Name);
        return true;
    }

    public bool BuyBusiness(string businessId)
    {
        var biz = Find(businessId);
        if (biz is null) return false;

        var cost = biz.NextCost;
        if (cost.IsNaN || Cash < cost) return false;

        Cash = SanitizeMoney(Cash - cost);
        biz.Owned++;
        logger.LogInformation("Bought {Business} #{Count} for {Cost}", biz.Name, biz.Owned, cost.ToCanonicalString());
        AutoStart(biz);
        return true;
    }

    public long BuyMultiple(string businessId, long count)
    {
        var biz = Find(businessId);
        if (biz is null || count <= 0L) return 0L;

        var toBuy = Math.Min(count, biz.AffordableCount(Cash));
        if (toBuy <= 0L) return 0L;

        var totalCost = biz.CostOf(toBuy);
        const int BackoffSafetyBound = 32;
        var backoffSteps = 0;
        while (toBuy > 0L && Cash < totalCost && backoffSteps < BackoffSafetyBound)
        {
            toBuy--;
            backoffSteps++;
            totalCost = biz.CostOf(toBuy);
        }

        if (toBuy <= 0L || Cash < totalCost) return 0L;

        Cash = SanitizeMoney(Cash - totalCost);
        biz.Owned += toBuy;
        logger.LogInformation("Bulk bought {Count} of {Business} for {Cost} (now {Total})",
            toBuy, biz.Name, totalCost.ToCanonicalString(), biz.Owned);
        AutoStart(biz);
        return toBuy;
    }

    public long BuyMax(string businessId) => BuyMultiple(businessId, long.MaxValue);

    public bool BuyManager(string businessId)
    {
        var biz = Find(businessId);
        if (biz is null || biz.HasManager) return false;

        var cost = GameBalance.ManagerCost(biz);
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

    public (BigDouble angels, bool success) Prestige()
    {
        var newAngels = PotentialAngels;
        if (newAngels < BigDouble.One)
        {
            logger.LogInformation("Prestige rejected: not enough new angels ({Angels})", newAngels.ToCanonicalString());
            return (BigDouble.Zero, false);
        }

        AngelInvestors = SanitizeAngels(AngelInvestors + newAngels);
        PrestigeCount++;
        Cash = new BigDouble(GameBalance.StartingCash);
        Businesses = BusinessDefinitions.CreateDefaults();

        logger.LogInformation("Prestige #{Count}! Gained {Angels} angels", PrestigeCount, newAngels.ToCanonicalString());
        return (newAngels, true);
    }

    public BigDouble AngelBonus => GameBalance.AngelBonusFor(AngelInvestors);

    public BigDouble PotentialAngels
    {
        get
        {
            var gain = CalculateAngels(LifetimeEarnings) - AngelInvestors;
            return gain.Sign > 0 ? gain : BigDouble.Zero;
        }
    }

    public BigDouble PrestigeIncomeMultiplier =>
        GameBalance.AngelBonusFor(AngelInvestors + PotentialAngels) / AngelBonus;

    public BigDouble CrossBusinessSpeedMultiplier =>
        CrossBusinessSpeedBonus.CalculateSpeedMultiplier(MinOwnedAcrossBusinesses);

    public long MinOwnedAcrossBusinesses
    {
        get
        {
            if (Businesses.Count == 0) return 0L;
            var minOwned = long.MaxValue;
            foreach (var biz in Businesses)
            {
                if (biz.Owned < minOwned) minOwned = biz.Owned;
            }
            return minOwned < 0L ? 0L : minOwned;
        }
    }

    public BigDouble IncomePerSecond
    {
        get
        {
            var total = BigDouble.Zero;
            foreach (var biz in Businesses)
            {
                if (biz.HasManager && biz.Owned > 0) total += biz.RevenuePerSecond;
            }
            return total * AngelBonus * CrossBusinessSpeedMultiplier;
        }
    }

    public static BigDouble CalculateAngels(BigDouble lifetimeEarnings)
    {
        if (lifetimeEarnings.IsNaN || lifetimeEarnings.IsInfinity || lifetimeEarnings.Sign < 0) return BigDouble.Zero;
        if (lifetimeEarnings < PrestigeMinLifetime) return BigDouble.Zero;
        return (AngelScale * (lifetimeEarnings / AngelLifetimeDivisor).Sqrt()).Floor();
    }

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
        return total * AngelBonus * CrossBusinessSpeedMultiplier;
    }

    private Business? Find(string businessId) => Businesses.FirstOrDefault(b => b.Id == businessId);

    private static void AutoStart(Business biz)
    {
        if (biz.HasManager && !biz.IsRunning)
        {
            biz.IsRunning = true;
            biz.ProgressPercent = 0;
        }
    }

    private static BigDouble SanitizeMoney(BigDouble value)
    {
        if (value.IsNaN || value.IsInfinity || value.Sign < 0) return BigDouble.Zero;
        return value;
    }

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
            var data = JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? [];
            foreach (var biz in Businesses)
                if (data.TryGetValue(biz.Id, out var owned))
                    biz.Owned = Math.Max(0L, owned);
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
                    if (has && biz.Owned > 0) biz.IsRunning = true;
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

    public string ExportToString()
    {
        var data = new Dictionary<string, object>
        {
            ["v"] = 2,
            ["cash"] = SanitizeMoney(Cash).ToCanonicalString(),
            ["lifetime"] = SanitizeMoney(LifetimeEarnings).ToCanonicalString(),
            ["angels"] = SanitizeAngels(AngelInvestors).ToCanonicalString(),
            ["prestige"] = PrestigeCount,
            ["businesses"] = Businesses.ToDictionary(b => b.Id, b => b.Owned),
            ["managers"] = Businesses.ToDictionary(b => b.Id, b => b.HasManager),
            ["timestamp"] = _time.GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
        };
        var json = JsonSerializer.Serialize(data);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool ImportFromString(string encoded)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Trim()));
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (data is null) return false;

            Cash = SanitizeMoney(ReadBigDouble(data, "cash"));
            LifetimeEarnings = SanitizeMoney(ReadBigDouble(data, "lifetime"));
            AngelInvestors = SanitizeAngels(ReadBigDouble(data, "angels"));
            PrestigeCount = data.TryGetValue("prestige", out var prEl) ? prEl.GetInt32() : 0;

            Businesses = BusinessDefinitions.CreateDefaults();

            if (data.TryGetValue("businesses", out var bizEl))
            {
                var bizData = JsonSerializer.Deserialize<Dictionary<string, long>>(bizEl.GetRawText()) ?? [];
                foreach (var biz in Businesses)
                    if (bizData.TryGetValue(biz.Id, out var owned))
                        biz.Owned = Math.Max(0L, owned);
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
