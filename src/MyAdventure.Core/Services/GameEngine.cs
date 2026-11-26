using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Saves;

namespace MyAdventure.Core.Services;

public sealed partial class GameEngine
{
    public const string TelemetryName = "MyAdventure.GameEngine";

    private const double MinimumOfflineGapSeconds = 1.0;
    private const int BulkBuyBackoffLimit = 32;

    private static readonly ActivitySource ActivitySource = new(TelemetryName);
    private static readonly Meter GameMeter = new("MyAdventure.Game");
    private static readonly Counter<long> TickCounter = GameMeter.CreateCounter<long>("game.ticks");
    private static readonly Counter<long> PurchaseCounter = GameMeter.CreateCounter<long>("game.purchases");
    private static readonly Counter<double> EarningsCounter = GameMeter.CreateCounter<double>("game.earnings");
    private static readonly Histogram<double> TickDuration = GameMeter.CreateHistogram<double>("game.tick_duration_ms");

    private static readonly BigDouble PrestigeMinLifetime = new(GameBalance.PrestigeMinLifetime);
    private static readonly BigDouble AngelLifetimeDivisor = new(GameBalance.AngelLifetimeDivisor);
    private static readonly BigDouble AngelScale = new(GameBalance.AngelScale);

    private readonly IGameStateRepository _repository;
    private readonly ILogger<GameEngine> _logger;
    private readonly TimeProvider _time;

    public GameEngine(IGameStateRepository repository, ILogger<GameEngine> logger, TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    public BigDouble Cash { get; internal set; }
    public BigDouble LifetimeEarnings { get; internal set; }
    public BigDouble AngelInvestors { get; internal set; }
    public int PrestigeCount { get; private set; }
    public IReadOnlyList<Business> Businesses { get; internal set; } = BusinessDefinitions.CreateDefaults();

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

    public long MinOwnedAcrossBusinesses =>
        Businesses.Count == 0 ? 0L : Math.Max(0L, Businesses.Min(b => b.Owned));

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

    private BigDouble EarningsBonus => AngelBonus * CrossBusinessSpeedMultiplier;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("LoadGame");

        if (await _repository.LoadAsync(ct) is not { } snapshot)
        {
            Cash = new BigDouble(GameBalance.StartingCash);
            LogStartingFresh(GameBalance.StartingCash);
            return;
        }

        Restore(snapshot);

        var elapsed = _time.GetUtcNow().UtcDateTime - snapshot.LastPlayedAt;
        var earned = ApplyOfflineEarnings(elapsed);
        if (earned.Sign > 0) LogOfflineEarnings(earned.ToCanonicalString(), elapsed.TotalSeconds);

        activity?.SetTag("cash", Cash.ToCanonicalString());
        activity?.SetTag("businesses_owned", Businesses.Count(b => b.Owned > 0));
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("SaveGame");
        var snapshot = CreateSnapshot();
        await _repository.SaveAsync(snapshot, ct);
        LogSaved(snapshot.Cash.ToCanonicalString());
    }

    public GameSnapshot CreateSnapshot() => new()
    {
        Cash = SanitizeNonNegative(Cash),
        LifetimeEarnings = SanitizeNonNegative(LifetimeEarnings),
        AngelInvestors = SanitizeNonNegative(AngelInvestors),
        PrestigeCount = PrestigeCount,
        Owned = Businesses.ToDictionary(b => b.Id, b => b.Owned),
        Managers = Businesses.ToDictionary(b => b.Id, b => b.HasManager),
        LastPlayedAt = _time.GetUtcNow().UtcDateTime,
    };

    public void Restore(GameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Cash = SanitizeNonNegative(snapshot.Cash);
        LifetimeEarnings = SanitizeNonNegative(snapshot.LifetimeEarnings);
        AngelInvestors = SanitizeNonNegative(snapshot.AngelInvestors);
        PrestigeCount = Math.Max(0, snapshot.PrestigeCount);

        var businesses = BusinessDefinitions.CreateDefaults();
        foreach (var biz in businesses)
        {
            if (snapshot.Owned.TryGetValue(biz.Id, out var owned)) biz.Owned = Math.Max(0L, owned);
            if (snapshot.Managers.TryGetValue(biz.Id, out var hasManager))
            {
                biz.HasManager = hasManager;
                biz.IsRunning = hasManager && biz.Owned > 0;
            }
        }
        Businesses = businesses;
    }

    public void Tick(double deltaSeconds)
    {
        var started = Stopwatch.GetTimestamp();
        TickCounter.Add(1);

        var bonus = EarningsBonus;
        foreach (var biz in Businesses)
        {
            if (!biz.IsRunning || biz.Owned <= 0) continue;

            biz.ProgressPercent += deltaSeconds / biz.CycleTimeSeconds * 100.0;
            if (biz.ProgressPercent < 100.0) continue;

            var cycles = Math.Floor(biz.ProgressPercent / 100.0);
            Earn(biz.Revenue * new BigDouble(cycles) * bonus, new("business", biz.Id));
            biz.ProgressPercent %= 100.0;

            if (!biz.HasManager) biz.IsRunning = false;
        }

        TickDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public bool StartBusiness(string businessId)
    {
        if (Find(businessId) is not { Owned: > 0, IsRunning: false } biz) return false;

        biz.IsRunning = true;
        biz.ProgressPercent = 0;
        LogStarted(biz.Name);
        return true;
    }

    public bool BuyBusiness(string businessId)
    {
        if (Find(businessId) is not { } biz) return false;

        var cost = biz.NextCost;
        if (cost.IsNaN || Cash < cost) return false;

        Cash = SanitizeNonNegative(Cash - cost);
        biz.Owned++;
        RecordPurchase(biz, 1);
        LogBought(biz.Name, biz.Owned, cost.ToCanonicalString());
        AutoStart(biz);
        return true;
    }

    public long BuyMultiple(string businessId, long count)
    {
        if (Find(businessId) is not { } biz || count <= 0L) return 0L;

        var toBuy = Math.Min(count, biz.AffordableCount(Cash));
        if (toBuy <= 0L) return 0L;

        var totalCost = biz.CostOf(toBuy);
        for (var step = 0; toBuy > 0L && Cash < totalCost && step < BulkBuyBackoffLimit; step++)
        {
            toBuy--;
            totalCost = biz.CostOf(toBuy);
        }

        if (toBuy <= 0L || Cash < totalCost) return 0L;

        Cash = SanitizeNonNegative(Cash - totalCost);
        biz.Owned += toBuy;
        RecordPurchase(biz, toBuy);
        LogBulkBought(toBuy, biz.Name, totalCost.ToCanonicalString(), biz.Owned);
        AutoStart(biz);
        return toBuy;
    }

    public long BuyMax(string businessId) => BuyMultiple(businessId, long.MaxValue);

    public bool BuyManager(string businessId)
    {
        if (Find(businessId) is not { HasManager: false } biz) return false;

        var cost = GameBalance.ManagerCost(biz);
        if (Cash < cost) return false;

        Cash = SanitizeNonNegative(Cash - cost);
        biz.HasManager = true;
        AutoStart(biz);
        LogManagerBought(biz.Name);
        return true;
    }

    public (BigDouble angels, bool success) Prestige()
    {
        using var activity = ActivitySource.StartActivity("Prestige");

        var newAngels = PotentialAngels;
        if (newAngels < BigDouble.One)
        {
            LogPrestigeRejected(newAngels.ToCanonicalString());
            return (BigDouble.Zero, false);
        }

        AngelInvestors = SanitizeNonNegative(AngelInvestors + newAngels);
        PrestigeCount++;
        Cash = new BigDouble(GameBalance.StartingCash);
        Businesses = BusinessDefinitions.CreateDefaults();

        activity?.SetTag("angels", newAngels.ToCanonicalString());
        LogPrestiged(PrestigeCount, newAngels.ToCanonicalString());
        return (newAngels, true);
    }

    public static BigDouble CalculateAngels(BigDouble lifetimeEarnings)
    {
        if (!lifetimeEarnings.IsFinite || lifetimeEarnings.Sign < 0) return BigDouble.Zero;
        if (lifetimeEarnings < PrestigeMinLifetime) return BigDouble.Zero;
        return (AngelScale * (lifetimeEarnings / AngelLifetimeDivisor).Sqrt()).Floor();
    }

    public BigDouble ApplyOfflineEarnings(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= MinimumOfflineGapSeconds) return BigDouble.Zero;

        var total = BigDouble.Zero;
        foreach (var biz in Businesses)
        {
            if (biz.HasManager && biz.Owned > 0) total += biz.Revenue * new BigDouble(elapsed.TotalSeconds / biz.CycleTimeSeconds);
        }

        var earned = total * AngelBonus * CrossBusinessSpeedMultiplier;
        if (earned.Sign <= 0) return BigDouble.Zero;

        Earn(earned, new("source", "offline"));
        return earned;
    }

    public string ExportToString()
    {
        using var activity = ActivitySource.StartActivity("ExportGame");
        return SaveCodec.Encode(CreateSnapshot(), _time.GetUtcNow().UtcDateTime);
    }

    public bool ImportFromString(string encoded)
    {
        using var activity = ActivitySource.StartActivity("ImportGame");

        if (!SaveCodec.TryDecode(encoded, out var snapshot))
        {
            LogImportRejected();
            return false;
        }

        Restore(snapshot);
        LogImported(Cash.ToCanonicalString(), AngelInvestors.ToCanonicalString());
        return true;
    }

    private void Earn(BigDouble amount, KeyValuePair<string, object?> tag)
    {
        Cash = SanitizeNonNegative(Cash + amount);
        LifetimeEarnings = SanitizeNonNegative(LifetimeEarnings + amount);
        EarningsCounter.Add(amount.ToDouble(), tag);
    }

    private Business? Find(string businessId) => Businesses.FirstOrDefault(b => b.Id == businessId);

    private static void RecordPurchase(Business biz, long count) =>
        PurchaseCounter.Add(count, new KeyValuePair<string, object?>("business", biz.Id));

    private static void AutoStart(Business biz)
    {
        if (!biz.HasManager || biz.Owned <= 0 || biz.IsRunning) return;
        biz.IsRunning = true;
        biz.ProgressPercent = 0;
    }

    private static BigDouble SanitizeNonNegative(BigDouble value) =>
        value.IsFinite && value.Sign >= 0 ? value : BigDouble.Zero;

    [LoggerMessage(LogLevel.Information, "No saved game found, starting fresh with {Cash}")]
    private partial void LogStartingFresh(double cash);

    [LoggerMessage(LogLevel.Information, "Applied offline earnings on load: {Earnings} for {Seconds:F0}s away")]
    private partial void LogOfflineEarnings(string earnings, double seconds);

    [LoggerMessage(LogLevel.Debug, "Game saved. Cash: {Cash}")]
    private partial void LogSaved(string cash);

    [LoggerMessage(LogLevel.Debug, "Started business: {Business}")]
    private partial void LogStarted(string business);

    [LoggerMessage(LogLevel.Information, "Bought {Business} #{Count} for {Cost}")]
    private partial void LogBought(string business, long count, string cost);

    [LoggerMessage(LogLevel.Information, "Bulk bought {Count} of {Business} for {Cost} (now {Total})")]
    private partial void LogBulkBought(long count, string business, string cost, long total);

    [LoggerMessage(LogLevel.Information, "Bought manager for {Business}")]
    private partial void LogManagerBought(string business);

    [LoggerMessage(LogLevel.Information, "Prestige rejected: not enough new angels ({Angels})")]
    private partial void LogPrestigeRejected(string angels);

    [LoggerMessage(LogLevel.Information, "Prestige #{Count}! Gained {Angels} angels")]
    private partial void LogPrestiged(int count, string angels);

    [LoggerMessage(LogLevel.Warning, "Rejected an import string that could not be decoded")]
    private partial void LogImportRejected();

    [LoggerMessage(LogLevel.Information, "Imported game state. Cash: {Cash}, Angels: {Angels}")]
    private partial void LogImported(string cash, string angels);
}
