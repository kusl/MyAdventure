using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Saves;

namespace MyAdventure.Testing;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public ManualTimeProvider() : this(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class InMemoryGameStateRepository(GameSnapshot? initial = null) : IGameStateRepository
{
    public GameSnapshot? Current { get; private set; } = initial;

    public List<GameSnapshot> Saved { get; } = [];

    public Task<GameSnapshot?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task SaveAsync(GameSnapshot snapshot, CancellationToken ct = default)
    {
        Current = snapshot;
        Saved.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        Current = null;
        return Task.CompletedTask;
    }
}

public sealed class InMemorySettingsRepository(PlayerSettings? initial = null) : ISettingsRepository
{
    public PlayerSettings Current { get; private set; } = initial ?? PlayerSettings.Default;

    public int SaveCount { get; private set; }

    public Task<PlayerSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task SaveAsync(PlayerSettings settings, CancellationToken ct = default)
    {
        Current = settings;
        SaveCount++;
        return Task.CompletedTask;
    }
}
