using Microsoft.EntityFrameworkCore;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;

namespace MyAdventure.Infrastructure.Repositories;

public sealed class SettingsRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    DatabaseInitializer initializer) : ISettingsRepository
{
    public async Task<PlayerSettings> LoadAsync(CancellationToken ct = default)
    {
        await initializer.InitializeAsync(ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var record = await db.PlayerSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlayerSettingsRecord.SingletonId, ct);
        return record is null ? PlayerSettings.Default : new PlayerSettings(record.SoundEnabled, record.HapticsEnabled);
    }

    public async Task SaveAsync(PlayerSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await initializer.InitializeAsync(ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var record = await db.PlayerSettings.FirstOrDefaultAsync(s => s.Id == PlayerSettingsRecord.SingletonId, ct);
        if (record is null)
        {
            record = new PlayerSettingsRecord();
            db.PlayerSettings.Add(record);
        }

        record.SoundEnabled = settings.SoundEnabled;
        record.HapticsEnabled = settings.HapticsEnabled;
        await db.SaveChangesAsync(ct);
    }
}
