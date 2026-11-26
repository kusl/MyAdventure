using MyAdventure.Core.Entities;

namespace MyAdventure.Core.Interfaces;

public interface ISettingsRepository
{
    Task<PlayerSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(PlayerSettings settings, CancellationToken ct = default);
}
