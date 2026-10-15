using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Data;
using Shouldly;

namespace MyAdventure.Integration.Tests;

public class SchemaMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public SchemaMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"myadventure-test-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task InitializeDatabaseAsync_FreshDatabase_CreatesNewSchema()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(provider);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.Current.CancellationToken);
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
        await CreateLegacyDatabaseAsync(_dbPath, cash: 1234.5, lifetime: 9999.99, angels: 50.0);

        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();
        await DependencyInjection.InitializeDatabaseAsync(provider);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        var cols = await GetColumnsAsync(conn, "GameStates");
        cols.ShouldContain("CashText");
        cols.ShouldNotContain("Cash");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CashText, LifetimeEarningsText, AngelInvestorsText FROM GameStates LIMIT 1";
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.GetString(0).ShouldStartWith("1234.5");
        reader.GetString(1).ShouldStartWith("9999.99");
        reader.GetString(2).ShouldStartWith("50");
    }

    [Fact]
    public async Task InitializeDatabaseAsync_RunTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(provider);
        await DependencyInjection.InitializeDatabaseAsync(provider);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        var cols = await GetColumnsAsync(conn, "GameStates");
        cols.ShouldContain("CashText");
    }

    private static async Task CreateLegacyDatabaseAsync(string dbPath, double cash, double lifetime, double angels)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(TestContext.Current.CancellationToken);

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
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection conn, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            result.Add(reader.GetString(1));
        return result;
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}
