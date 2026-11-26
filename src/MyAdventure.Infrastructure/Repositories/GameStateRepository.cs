using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Saves;
using MyAdventure.Infrastructure.Data;

namespace MyAdventure.Infrastructure.Repositories;

public sealed class GameStateRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    DatabaseInitializer initializer,
    TimeProvider time,
    ILogger<GameStateRepository> logger) : IGameStateRepository
{
    public async Task<GameSnapshot?> LoadAsync(CancellationToken ct = default)
    {
        await initializer.InitializeAsync(ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var record = await db.GameStates.AsNoTracking().OrderByDescending(g => g.UpdatedAt).FirstOrDefaultAsync(ct);
        return record is null ? null : ToSnapshot(record);
    }

    public async Task SaveAsync(GameSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await initializer.InitializeAsync(ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var now = time.GetUtcNow().UtcDateTime;
        var record = await db.GameStates.OrderByDescending(g => g.UpdatedAt).FirstOrDefaultAsync(ct);
        if (record is null)
        {
            record = new GameStateRecord { CreatedAt = now };
            db.GameStates.Add(record);
        }

        record.CashText = snapshot.Cash.ToCanonicalString();
        record.LifetimeEarningsText = snapshot.LifetimeEarnings.ToCanonicalString();
        record.AngelInvestorsText = snapshot.AngelInvestors.ToCanonicalString();
        record.PrestigeCount = snapshot.PrestigeCount;
        record.BusinessDataJson = JsonSerializer.Serialize(new Dictionary<string, long>(snapshot.Owned), OwnershipJsonContext.Default.DictionaryStringInt64);
        record.ManagerDataJson = JsonSerializer.Serialize(new Dictionary<string, bool>(snapshot.Managers), OwnershipJsonContext.Default.DictionaryStringBoolean);
        record.LastPlayedAt = snapshot.LastPlayedAt;
        record.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogDebug("Game state saved");
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        await initializer.InitializeAsync(ct);
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await db.GameStates.ExecuteDeleteAsync(ct);
        logger.LogInformation("All game states deleted");
    }

    private GameSnapshot ToSnapshot(GameStateRecord record) => new()
    {
        Cash = ParseAmount(record.CashText),
        LifetimeEarnings = ParseAmount(record.LifetimeEarningsText),
        AngelInvestors = ParseAmount(record.AngelInvestorsText),
        PrestigeCount = record.PrestigeCount,
        Owned = ParseMap(record.BusinessDataJson, OwnershipJsonContext.Default.DictionaryStringInt64),
        Managers = ParseMap(record.ManagerDataJson, OwnershipJsonContext.Default.DictionaryStringBoolean),
        LastPlayedAt = DateTime.SpecifyKind(record.LastPlayedAt, DateTimeKind.Utc),
    };

    private static BigDouble ParseAmount(string text) =>
        BigDouble.TryParse(text, out var value) ? value : BigDouble.Zero;

    private Dictionary<string, T> ParseMap<T>(string json, JsonTypeInfo<Dictionary<string, T>> typeInfo)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse saved {Type} map", typeof(T).Name);
            return [];
        }
    }
}
