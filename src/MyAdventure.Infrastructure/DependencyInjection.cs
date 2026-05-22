using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? dbPath = null)
    {
        dbPath ??= GetDefaultDbPath();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<IGameStateRepository, GameStateRepository>();

        // OpenTelemetry
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService("MyAdventure", "1.0.0");

        services.AddLogging(logging =>
            logging.AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(resourceBuilder);
                otel.AddConsoleExporter();
            }));

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                .AddSource("MyAdventure.*")
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddMeter("MyAdventure.*")
                .AddRuntimeInstrumentation()
                .AddConsoleExporter());

        return services;
    }

    /// <summary>
    /// Initialize the SQLite database, applying an in-place schema migration
    /// to lift legacy REAL columns into the new TEXT (BigDouble) columns when
    /// an old v1 database is detected.
    /// <para>
    /// The BigDouble migration renamed the three numeric columns
    /// (<c>Cash</c>, <c>LifetimeEarnings</c>, <c>AngelInvestors</c>) to their
    /// <c>*Text</c> counterparts. We migrate in-place rather than dropping
    /// the table so any existing player progress is preserved across the
    /// schema change without requiring an export/import. The migration runs
    /// in a single transaction so a crash mid-migration leaves the old
    /// schema intact (no half-migrated database).
    /// </para>
    /// </summary>
    public static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DbInit");

        // Run the schema migration BEFORE EnsureCreated. EnsureCreated is a
        // no-op when the tables already exist, so it won't fix an old schema
        // for us — we have to do that explicitly.
        await MigrateLegacySchemaIfNeededAsync(db, logger);

        // Create the database / any genuinely missing tables idempotently.
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Inspect the GameStates table; if it has the legacy REAL columns,
    /// translate them to the new TEXT columns and drop the old ones.
    /// Idempotent — a fresh database or an already-migrated database
    /// passes straight through.
    /// </summary>
    private static async Task MigrateLegacySchemaIfNeededAsync(AppDbContext db, ILogger? logger)
    {
        var conn = (SqliteConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        // If the GameStates table doesn't exist at all yet, EnsureCreated
        // will make it with the new schema — nothing to migrate.
        var hasTable = await TableExistsAsync(conn, "GameStates");
        if (!hasTable) return;

        var columns = await GetColumnNamesAsync(conn, "GameStates");

        // Already migrated (or fresh-with-new-schema): the new columns exist.
        if (columns.Contains("CashText")) return;

        // No old columns either: nothing to do.
        if (!columns.Contains("Cash")) return;

        logger?.LogInformation("Migrating GameStates table to BigDouble TEXT schema");

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(conn, tx,
                "ALTER TABLE GameStates ADD COLUMN CashText TEXT NOT NULL DEFAULT '0'");
            await ExecuteAsync(conn, tx,
                "ALTER TABLE GameStates ADD COLUMN LifetimeEarningsText TEXT NOT NULL DEFAULT '0'");
            await ExecuteAsync(conn, tx,
                "ALTER TABLE GameStates ADD COLUMN AngelInvestorsText TEXT NOT NULL DEFAULT '0'");

            // SQLite's CAST(double AS TEXT) produces an InvariantCulture
            // string representation that BigDouble.Parse will happily
            // round-trip (it falls back to plain double.Parse for any
            // numeric string that doesn't look like the canonical form).
            await ExecuteAsync(conn, tx,
                "UPDATE GameStates SET " +
                "CashText = CAST(Cash AS TEXT), " +
                "LifetimeEarningsText = CAST(LifetimeEarnings AS TEXT), " +
                "AngelInvestorsText = CAST(AngelInvestors AS TEXT)");

            // SQLite 3.35+ (EF Core 10 ships with a much newer version)
            // supports ALTER TABLE DROP COLUMN, so we don't need the
            // historical table-rebuild dance.
            await ExecuteAsync(conn, tx, "ALTER TABLE GameStates DROP COLUMN Cash");
            await ExecuteAsync(conn, tx, "ALTER TABLE GameStates DROP COLUMN LifetimeEarnings");
            await ExecuteAsync(conn, tx, "ALTER TABLE GameStates DROP COLUMN AngelInvestors");

            await tx.CommitAsync();
            logger?.LogInformation("BigDouble migration complete");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger?.LogError(ex, "BigDouble migration failed; legacy schema retained");
            throw;
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name";
        cmd.Parameters.AddWithValue("$name", table);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqliteConnection conn, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        // PRAGMA table_info doesn't accept parameter binding for the table
        // name; the value comes from a trusted constant here, not user input.
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Column index 1 is the column name in PRAGMA table_info's output.
            result.Add(reader.GetString(1));
        }
        return result;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetDefaultDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyAdventure");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "myadventure.db");
    }
}
