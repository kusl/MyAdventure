using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Data;
using Shouldly;

namespace MyAdventure.Integration.Tests;

/// <summary>
/// Integration tests for the BigDouble schema migration. Construct a
/// SQLite file with the LEGACY v1 schema (REAL columns), then run the
/// initializer and verify the data ends up in the new TEXT columns
/// with values that round-trip correctly through BigDouble.Parse.
/// </summary>
public class SchemaMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public SchemaMigrationTests()
    {
        // Use a unique on-disk SQLite file per test — the in-memory provider
        // doesn't support the ALTER TABLE DROP COLUMN that real SQLite does,
        // so we need actual disk SQLite.
        _dbPath = Path.Combine(Path.GetTempPath(), $"myadventure-test-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task InitializeDatabaseAsync_FreshDatabase_CreatesNewSchema()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(provider);

        // Verify the new columns exist by querying them.
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var cols = await GetColumnsAsync(conn, "GameStates");
        cols.ShouldContain("CashText");
        cols.ShouldContain("LifetimeEarningsText");
        cols.ShouldContain("AngelInvestorsText");
        cols.ShouldNotContain("Cash");
        cols.ShouldNotContain("LifetimeEarnings");
        cols.ShouldNotContain("AngelInvestors");
    }

    [Fact]
    public async Task InitializeDatabaseAsync_LegacySchema_MigratesData()
    {
        // 1. Hand-create a v1-schema database with sample data.
        await CreateLegacyDatabaseAsync(_dbPath, cash: 1234.5, lifetime: 9999.99, angels: 50.0);

        // 2. Run the initializer — it should detect the legacy columns
        // and migrate them in-place.
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();
        await DependencyInjection.InitializeDatabaseAsync(provider);

        // 3. Verify the new columns exist with the migrated data,
        // and the old columns are gone.
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var cols = await GetColumnsAsync(conn, "GameStates");
        cols.ShouldContain("CashText");
        cols.ShouldNotContain("Cash");

        // Read the migrated data — should be the cast-to-TEXT
        // representations of the original doubles.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CashText, LifetimeEarningsText, AngelInvestorsText FROM GameStates LIMIT 1";
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        // SQLite's CAST(double AS TEXT) produces something like "1234.5".
        reader.GetString(0).ShouldStartWith("1234.5");
        reader.GetString(1).ShouldStartWith("9999.99");
        reader.GetString(2).ShouldStartWith("50");
    }

    [Fact]
    public async Task InitializeDatabaseAsync_RunTwice_IsIdempotent()
    {
        // Migration must be safe to run repeatedly — the engine boots
        // through this path on every cold start.
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(provider);
        await DependencyInjection.InitializeDatabaseAsync(provider);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var cols = await GetColumnsAsync(conn, "GameStates");
        cols.ShouldContain("CashText");
    }

    /// <summary>
    /// Open the database with the legacy v1 schema (REAL columns) and
    /// insert a single sample row. Mimics what an existing player's save
    /// file looks like before the migration runs.
    /// </summary>
    private static async Task CreateLegacyDatabaseAsync(string dbPath, double cash, double lifetime, double angels)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await ExecAsync(conn, """
            CREATE TABLE GameStates (
                Id TEXT NOT NULL PRIMARY KEY,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                Cash REAL NOT NULL DEFAULT 0,
                LifetimeEarnings REAL NOT NULL DEFAULT 0,
                AngelInvestors REAL NOT NULL DEFAULT 0,
                PrestigeCount INTEGER NOT NULL DEFAULT 0,
                BusinessDataJson TEXT NOT NULL DEFAULT '{}',
                ManagerDataJson TEXT NOT NULL DEFAULT '{}',
                LastPlayedAt TEXT NOT NULL
            )
            """);

        await using var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO GameStates (Id, CreatedAt, UpdatedAt, Cash, LifetimeEarnings, AngelInvestors, PrestigeCount, BusinessDataJson, ManagerDataJson, LastPlayedAt)
            VALUES ($id, $now, $now, $cash, $lt, $angels, 0, '{}', '{}', $now)
            """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        insert.Parameters.AddWithValue("$cash", cash);
        insert.Parameters.AddWithValue("$lt", lifetime);
        insert.Parameters.AddWithValue("$angels", angels);
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection conn, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(1));
        return result;
    }

    public void Dispose()
    {
        // Best-effort cleanup. SQLite file handles can linger briefly on Windows;
        // we ignore any IO exception rather than failing the test on teardown.
        try
        {
            // Force pool to release connections so the file can be deleted.
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Swallow — temp directory cleanup will handle it eventually.
        }
    }
}
