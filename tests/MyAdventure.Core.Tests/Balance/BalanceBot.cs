using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;

namespace MyAdventure.Core.Tests.Balance;

public sealed record PrestigeEvent(int Number, double AtSeconds, double RunSeconds, double AngelsLog10, double LifetimeLog10);

public sealed record BalanceReport(
    double? FirstManagerSeconds,
    IReadOnlyList<PrestigeEvent> Prestiges,
    IReadOnlyList<(double AtSeconds, double IncomeLog10)> IncomeSamples,
    double EndSeconds,
    int Steps);

public sealed class BalanceBot
{
    private const double ClickIntervalSeconds = 1.0;
    private const double MinimumJumpSeconds = 1.01;
    private readonly GameEngine _engine;
    private readonly double _prestigeAtIncomeMultiplier;
    private double _now;
    private double _runStart;
    private double? _firstManager;
    private int _steps;
    private readonly List<PrestigeEvent> _prestiges = [];
    private readonly List<(double, double)> _samples = [];

    private sealed class NoSaveRepository : IGameStateRepository
    {
        public Task<GameState?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult<GameState?>(null);
        public Task SaveAsync(GameState state, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public BalanceBot(double prestigeAtIncomeMultiplier = 2.0, double seedLifetimeLog10 = double.NaN)
    {
        _prestigeAtIncomeMultiplier = prestigeAtIncomeMultiplier;
        _engine = new GameEngine(new NoSaveRepository(), NullLogger<GameEngine>.Instance);
        _engine.LoadAsync().GetAwaiter().GetResult();
        if (double.IsFinite(seedLifetimeLog10)) Seed(seedLifetimeLog10);
    }

    private void Seed(double lifetimeLog10)
    {
        var lifetime = new BigDouble(10.0).Pow(lifetimeLog10);
        var angels = GameEngine.CalculateAngels(lifetime);
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["v"] = 2,
            ["cash"] = GameBalance.StartingCash.ToString(CultureInfo.InvariantCulture),
            ["lifetime"] = lifetime.ToCanonicalString(),
            ["angels"] = angels.ToCanonicalString(),
            ["prestige"] = 1,
        });
        if (!_engine.ImportFromString(Convert.ToBase64String(Encoding.UTF8.GetBytes(json))))
            throw new InvalidOperationException("Seed import failed");
    }

    public GameEngine Engine => _engine;

    public BalanceReport Play(double horizonSeconds, int maxPrestiges = int.MaxValue, int maxSteps = 200_000, IReadOnlyList<double>? sampleAt = null)
    {
        var samples = new Queue<double>(sampleAt ?? []);
        while (_now < horizonSeconds && _prestiges.Count < maxPrestiges && _steps < maxSteps)
        {
            _steps++;
            while (samples.Count > 0 && samples.Peek() <= _now)
            {
                samples.Dequeue();
                _samples.Add((_now, Log10(_engine.IncomePerSecond)));
            }

            if (_prestigeAtIncomeMultiplier > 0 && _engine.PotentialAngels >= BigDouble.One &&
                _engine.PrestigeIncomeMultiplier >= new BigDouble(_prestigeAtIncomeMultiplier))
            {
                var (_, ok) = _engine.Prestige();
                if (ok)
                {
                    _prestiges.Add(new PrestigeEvent(_engine.PrestigeCount, _now, _now - _runStart,
                        Log10(_engine.AngelInvestors), Log10(_engine.LifetimeEarnings)));
                    _runStart = _now;
                    continue;
                }
            }

            var managed = _engine.Businesses.Any(b => b.HasManager);
            var income = managed ? _engine.IncomePerSecond : ClickIncome(_engine.Businesses);
            var choice = Choose(income, managed);

            if (!managed)
            {
                if (choice is not null && _engine.Cash >= choice.Value.Cost)
                {
                    Execute(choice.Value);
                    continue;
                }
                foreach (var b in _engine.Businesses) _engine.StartBusiness(b.Id);
                _engine.Tick(ClickIntervalSeconds);
                _now += ClickIntervalSeconds;
                continue;
            }

            if (choice is null)
            {
                Advance(horizonSeconds - _now);
                break;
            }

            var cost = choice.Value.Cost;
            if (_engine.Cash < cost)
            {
                var wait = ((cost - _engine.Cash) / income).ToDouble();
                if (!double.IsFinite(wait) || _now + wait > horizonSeconds)
                {
                    Advance(horizonSeconds - _now);
                    break;
                }
                Advance(Math.Max(wait * 1.000001, MinimumJumpSeconds));
                if (_engine.Cash < cost) continue;
            }
            Execute(choice.Value);
        }

        while (samples.Count > 0 && samples.Peek() <= _now)
        {
            samples.Dequeue();
            _samples.Add((_now, Log10(_engine.IncomePerSecond)));
        }

        return new BalanceReport(_firstManager, _prestiges.ToList(), _samples.ToList(), _now, _steps);
    }

    private void Advance(double seconds)
    {
        if (seconds <= 0) return;
        if (seconds >= MinimumJumpSeconds) _engine.ApplyOfflineEarnings(TimeSpan.FromSeconds(seconds));
        _now += seconds;
    }

    private void Execute((BigDouble Cost, double Score, string Id, long Count, bool Manager) choice)
    {
        if (choice.Count > 0 && _engine.BuyMultiple(choice.Id, choice.Count) < choice.Count) return;
        if (choice.Manager && _engine.BuyManager(choice.Id)) _firstManager ??= _now;
    }

    private (BigDouble Cost, double Score, string Id, long Count, bool Manager)? Choose(BigDouble income, bool managed)
    {
        var businesses = _engine.Businesses;
        var bonus = _engine.AngelBonus;
        var minOwned = _engine.MinOwnedAcrossBusinesses;
        var cross = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(minOwned);
        var baseSum = BigDouble.Zero;
        foreach (var b in businesses)
            baseSum += managed ? (b.HasManager && b.Owned > 0 ? b.RevenuePerSecond : BigDouble.Zero) : ClickRate(b);

        (BigDouble Cost, double Score, string Id, long Count, bool Manager)? best = null;
        var cash = _engine.Cash;

        foreach (var b in businesses)
        {
            foreach (var target in Targets(b, minOwned))
            {
                var count = target - b.Owned;
                if (count <= 0) continue;
                var cost = b.CostOf(count);
                var after = b with { Owned = target };
                var newMin = businesses.Min(x => x.Id == b.Id ? target : x.Owned);
                var newCross = CrossBusinessSpeedBonus.CalculateSpeedMultiplier(newMin);
                var withManager = managed && !b.HasManager;
                if (withManager) cost += GameBalance.ManagerCost(b);
                var before = managed ? (b.HasManager ? b.RevenuePerSecond : BigDouble.Zero) : ClickRate(b);
                var gained = managed ? after.RevenuePerSecond : ClickRate(after);
                var newIncome = (baseSum - before + gained) * newCross * bonus;
                Consider(ref best, cost, newIncome - income, income, cash, b.Id, count, withManager);
            }

            if (!b.HasManager && b.Owned > 0)
            {
                var gain = (managed ? b.RevenuePerSecond : b.RevenuePerSecond - ClickRate(b)) * cross * bonus;
                Consider(ref best, GameBalance.ManagerCost(b), gain, income, cash, b.Id, 0, true);
            }
        }
        return best;
    }

    private static void Consider(ref (BigDouble Cost, double Score, string Id, long Count, bool Manager)? best,
        BigDouble cost, BigDouble gain, BigDouble income, BigDouble cash, string id, long count, bool manager)
    {
        if (gain.Sign <= 0) return;
        var payback = (cost / gain).ToDouble();
        var wait = cash >= cost ? 0.0 : income.Sign > 0 ? ((cost - cash) / income).ToDouble() : double.PositiveInfinity;
        var score = wait + payback;
        if (!double.IsFinite(score)) return;
        if (best is null || score < best.Value.Score) best = (cost, score, id, count, manager);
    }

    private static IEnumerable<long> Targets(Business b, long minOwned)
    {
        var owned = b.Owned;
        var doubling = Math.Max(1L, (long)Math.Round(Math.Log(2.0) / Math.Log(b.CostMultiplier)));
        yield return owned < 25 ? owned + 1 : owned + doubling;
        var rev = Milestone.NextMilestone(owned);
        if (rev is not null) yield return rev.Threshold;
        var spd = SpeedMilestone.NextSpeedMilestone(owned);
        if (spd is not null) yield return spd.Threshold;
        if (owned == minOwned)
        {
            var cross = CrossBusinessSpeedBonus.NextThreshold(minOwned);
            if (cross is not null) yield return cross.Value;
        }
    }

    private static BigDouble ClickRate(Business b)
    {
        if (b.Owned <= 0) return BigDouble.Zero;
        return b.Revenue / new BigDouble(Math.Max(b.CycleTimeSeconds, ClickIntervalSeconds));
    }

    private static BigDouble ClickIncome(IReadOnlyList<Business> businesses)
    {
        var total = BigDouble.Zero;
        foreach (var b in businesses) total += ClickRate(b);
        return total;
    }

    private static double Log10(BigDouble value) => value.Sign > 0 ? value.Log10() : double.NegativeInfinity;
}
