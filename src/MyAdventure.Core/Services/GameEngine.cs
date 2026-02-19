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

        Cash = state.Cash;
        LifetimeEarnings = state.LifetimeEarnings;
        AngelInvestors = state.AngelInvestors;
        PrestigeCount = state.PrestigeCount;

        ApplyBusinessData(state.BusinessDataJson);
        ApplyManagerData(state.ManagerDataJson);

        // Calculate offline earnings
        var elapsed = _time.GetUtcNow().UtcDateTime - state.LastPlayedAt;
        if (elapsed.TotalSeconds > 1)
        {
            var offlineEarnings = CalculateOfflineEarnings(elapsed);
            Cash += offlineEarnings;
            LifetimeEarnings += offlineEarnings;
            logger.LogInformation("Applied offline earnings: {Earnings:F2} for {Seconds:F0}s away",
                offlineEarnings, elapsed.TotalSeconds);
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
            Cash = Cash,
            LifetimeEarnings = LifetimeEarnings,
            AngelInvestors = AngelInvestors,
            PrestigeCount = PrestigeCount,
            BusinessDataJson = SerializeBusinessData(),
            ManagerDataJson = SerializeManagerData(),
            LastPlayedAt = _time.GetUtcNow().UtcDateTime
        };
        await repository.SaveAsync(state, ct);
        logger.LogDebug("Game saved. Cash: {Cash:F2}", Cash);
    }

    /// <summary>Process one game tick (called ~60fps from UI timer).</summary>
    public void Tick(double deltaSeconds)
    {
        var sw = Stopwatch.StartNew();
        TickCounter.Add(1);

        foreach (var biz in Businesses)
        {
            if (!biz.IsRunning || biz.Owned <= 0) continue;

            biz.ProgressPercent += (deltaSeconds / biz.CycleTimeSeconds) * 100.0;

            if (biz.ProgressPercent >= 100.0)
            {
                var cycles = (int)(biz.ProgressPercent / 100.0);
                var earned = biz.Revenue * cycles;
                Cash += earned;
                LifetimeEarnings += earned;
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
        if (Cash < cost) return false;

        Cash -= cost;
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

    /// <summary>Buy a manager for a business. Cost = 1000x base cost.</summary>
    public bool BuyManager(string businessId)
    {
        var biz = Businesses.FirstOrDefault(b => b.Id == businessId);
        if (biz is null || biz.HasManager) return false;

        var cost = biz.BaseCost * 1000;
        if (Cash < cost) return false;

        Cash -= cost;
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

        AngelInvestors += newAngels;
        PrestigeCount++;
        Cash = 0;
        LifetimeEarnings = 0;

        // Reset businesses
        var defaults = BusinessDefinitions.CreateDefaults();
        Businesses = defaults;

        logger.LogInformation("Prestige #{Count}! Gained {Angels:F0} angels", PrestigeCount, newAngels);
        return (newAngels, true);
    }

    /// <summary>Angel investor bonus: 2% per angel.</summary>
    public double AngelBonus => 1.0 + (AngelInvestors * 0.02);

    public static double CalculateAngels(double lifetimeEarnings) =>
        lifetimeEarnings >= 1e12 ? Math.Floor(150 * Math.Sqrt(lifetimeEarnings / 1e13)) : 0;

    private double CalculateOfflineEarnings(TimeSpan elapsed)
    {
        double total = 0;
        foreach (var biz in Businesses.Where(b => b.HasManager && b.Owned > 0))
        {
            var cycles = elapsed.TotalSeconds / biz.CycleTimeSeconds;
            total += biz.Revenue * cycles;
        }
        return total * AngelBonus;
    }

    private void ApplyBusinessData(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
            foreach (var biz in Businesses)
                if (data.TryGetValue(biz.Id, out var owned))
                    biz.Owned = owned;
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
}
