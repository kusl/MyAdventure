namespace MyAdventure.Core.Interfaces;

using MyAdventure.Core.Entities;

public interface IGameStateRepository
{
    Task<GameState?> GetLatestAsync(CancellationToken ct = default);
    Task SaveAsync(GameState state, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}
