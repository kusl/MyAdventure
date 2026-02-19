using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;

namespace MyAdventure.Infrastructure.Repositories;

public class GameStateRepository(
    AppDbContext db,
    ILogger<GameStateRepository> logger) : IGameStateRepository
{
    public async Task<GameState?> GetLatestAsync(CancellationToken ct = default)
    {
        logger.LogDebug("Loading latest game state");
        return await db.GameStates
            .OrderByDescending(g => g.UpdatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SaveAsync(GameState state, CancellationToken ct = default)
    {
        state.UpdatedAt = DateTime.UtcNow;

        var existing = await db.GameStates.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            db.GameStates.Add(state);
        }
        else
        {
            existing.Cash = state.Cash;
            existing.LifetimeEarnings = state.LifetimeEarnings;
            existing.AngelInvestors = state.AngelInvestors;
            existing.PrestigeCount = state.PrestigeCount;
            existing.BusinessDataJson = state.BusinessDataJson;
            existing.ManagerDataJson = state.ManagerDataJson;
            existing.LastPlayedAt = state.LastPlayedAt;
            existing.UpdatedAt = state.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
        logger.LogDebug("Game state saved");
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        db.GameStates.RemoveRange(db.GameStates);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("All game states deleted");
    }
}
