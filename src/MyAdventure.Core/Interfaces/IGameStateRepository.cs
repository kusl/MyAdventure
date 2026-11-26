using MyAdventure.Core.Saves;

namespace MyAdventure.Core.Interfaces;

public interface IGameStateRepository
{
    Task<GameSnapshot?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(GameSnapshot snapshot, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}
