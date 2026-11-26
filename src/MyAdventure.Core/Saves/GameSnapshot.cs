using MyAdventure.Core.Numerics;

namespace MyAdventure.Core.Saves;

public sealed record GameSnapshot
{
    public BigDouble Cash { get; init; }
    public BigDouble LifetimeEarnings { get; init; }
    public BigDouble AngelInvestors { get; init; }
    public int PrestigeCount { get; init; }
    public IReadOnlyDictionary<string, long> Owned { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, bool> Managers { get; init; } = new Dictionary<string, bool>();
    public DateTime LastPlayedAt { get; init; }
}
