using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyAdventure.Infrastructure.Data;

public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory, ILogger<DatabaseInitializer> logger)
{
    private readonly Lock _gate = new();
    private Task? _initialization;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_initialization is null || _initialization.IsFaulted || _initialization.IsCanceled)
            {
                _initialization = RunAsync(ct);
            }
            return _initialization;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            if (await SchemaUpgrader.TableExistsAsync(connection, "GameStates", ct))
            {
                await SchemaUpgrader.UpgradeAsync(connection, logger, ct);
            }

            await db.Database.EnsureCreatedAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
