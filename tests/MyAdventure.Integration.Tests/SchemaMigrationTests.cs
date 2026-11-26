using Microsoft.Data.Sqlite;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;
using Shouldly;

namespace MyAdventure.Integration.Tests;

public sealed class SchemaMigrationTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_FreshDatabase_CreatesCurrentSchema()
    {
        await _database.InitializeAsync();

        using var connection = _database.OpenConnection();
        var columns = await SchemaUpgrader.GetColumnNamesAsync(connection, "GameStates", Ct);
        columns.ShouldContain("CashText");
        columns.ShouldContain("LifetimeEarningsText");
        columns.ShouldContain("AngelInvestorsText");
        columns.ShouldNotContain("Cash");
        (await SchemaUpgrader.TableExistsAsync(connection, "PlayerSettings", Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Initialize_LegacyRealSchema_MigratesDataAndAddsSettings()
    {
        await CreateLegacyDatabaseAsync(cash: 1234.5, lifetime: 9999.99, angels: 50.0);

        await _database.InitializeAsync();

        using var connection = _database.OpenConnection();
        var columns = await SchemaUpgrader.GetColumnNamesAsync(connection, "GameStates", Ct);
        columns.ShouldContain("CashText");
        columns.ShouldNotContain("Cash");
        (await SchemaUpgrader.TableExistsAsync(connection, "PlayerSettings", Ct)).ShouldBeTrue();

        var snapshot = await _database.Get<IGameStateRepository>().LoadAsync(Ct);
        snapshot.ShouldNotBeNull();
        snapshot.Cash.ToDouble().ShouldBe(1234.5, tolerance: 1e-9);
        snapshot.LifetimeEarnings.ToDouble().ShouldBe(9999.99, tolerance: 1e-9);
        snapshot.AngelInvestors.ToDouble().ShouldBe(50.0, tolerance: 1e-9);
    }

    [Fact]
    public async Task Initialize_TextSchemaWithoutSettings_AddsSettingsAndKeepsProgress()
    {
        await CreatePreSettingsDatabaseAsync();

        await _database.InitializeAsync();

        var settings = await _database.Get<ISettingsRepository>().LoadAsync(Ct);
        settings.SoundEnabled.ShouldBeTrue();

        var snapshot = await _database.Get<IGameStateRepository>().LoadAsync(Ct);
        snapshot.ShouldNotBeNull();
        snapshot.Cash.ToCanonicalString().ShouldBe("4.2e100");
        snapshot.Owned["lemonade"].ShouldBe(7);
    }

    [Fact]
    public async Task Initialize_RunTwice_IsIdempotent()
    {
        await _database.InitializeAsync();
        await new DatabaseInitializer(
                _database.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync(Ct);

        using var connection = _database.OpenConnection();
        (await SchemaUpgrader.GetColumnNamesAsync(connection, "GameStates", Ct)).ShouldContain("CashText");
    }

    private async Task CreateLegacyDatabaseAsync(double cash, double lifetime, double angels)
    {
        using var connection = _database.OpenConnection();
        await ExecAsync(connection, """
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

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO GameStates (Id, CreatedAt, UpdatedAt, Cash, LifetimeEarnings, AngelInvestors, PrestigeCount, BusinessDataJson, ManagerDataJson, LastPlayedAt)
            VALUES ($id, $now, $now, $cash, $lt, $angels, 0, '{}', '{}', $now)
            """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        insert.Parameters.AddWithValue("$cash", cash);
        insert.Parameters.AddWithValue("$lt", lifetime);
        insert.Parameters.AddWithValue("$angels", angels);
        await insert.ExecuteNonQueryAsync(Ct);
    }

    private async Task CreatePreSettingsDatabaseAsync()
    {
        using var connection = _database.OpenConnection();
        await ExecAsync(connection, """
            CREATE TABLE "GameStates" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GameStates" PRIMARY KEY,
                "AngelInvestorsText" TEXT NOT NULL DEFAULT '0',
                "BusinessDataJson" TEXT NOT NULL DEFAULT '{}',
                "CashText" TEXT NOT NULL DEFAULT '0',
                "CreatedAt" TEXT NOT NULL,
                "LastPlayedAt" TEXT NOT NULL,
                "LifetimeEarningsText" TEXT NOT NULL DEFAULT '0',
                "ManagerDataJson" TEXT NOT NULL DEFAULT '{}',
                "PrestigeCount" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAt" TEXT NOT NULL
            )
            """);
        await ExecAsync(connection, $$"""
            INSERT INTO GameStates (Id, AngelInvestorsText, BusinessDataJson, CashText, CreatedAt, LastPlayedAt, LifetimeEarningsText, ManagerDataJson, PrestigeCount, UpdatedAt)
            VALUES ('{{Guid.NewGuid()}}', '0', '{"lemonade":7}', '4.2e100', '2026-01-01 00:00:00', '2026-01-01 00:00:00', '4.2e100', '{"lemonade":false}', 0, '2026-01-01 00:00:00')
            """);
    }

    private static async Task ExecAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    public void Dispose() => _database.Dispose();
}
