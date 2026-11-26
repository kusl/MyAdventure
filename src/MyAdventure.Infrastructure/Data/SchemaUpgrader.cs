using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyAdventure.Infrastructure.Data;

internal static class SchemaUpgrader
{
    private static readonly string[] LegacyRealToBigDoubleText =
    [
        "ALTER TABLE GameStates ADD COLUMN CashText TEXT NOT NULL DEFAULT '0'",
        "ALTER TABLE GameStates ADD COLUMN LifetimeEarningsText TEXT NOT NULL DEFAULT '0'",
        "ALTER TABLE GameStates ADD COLUMN AngelInvestorsText TEXT NOT NULL DEFAULT '0'",
        "UPDATE GameStates SET CashText = CAST(Cash AS TEXT), LifetimeEarningsText = CAST(LifetimeEarnings AS TEXT), AngelInvestorsText = CAST(AngelInvestors AS TEXT)",
        "ALTER TABLE GameStates DROP COLUMN Cash",
        "ALTER TABLE GameStates DROP COLUMN LifetimeEarnings",
        "ALTER TABLE GameStates DROP COLUMN AngelInvestors",
    ];

    private const string CreatePlayerSettings =
        """CREATE TABLE IF NOT EXISTS "PlayerSettings" ("Id" INTEGER NOT NULL CONSTRAINT "PK_PlayerSettings" PRIMARY KEY, "SoundEnabled" INTEGER NOT NULL, "HapticsEnabled" INTEGER NOT NULL)""";

    public static async Task UpgradeAsync(SqliteConnection connection, ILogger logger, CancellationToken ct)
    {
        var columns = await GetColumnNamesAsync(connection, "GameStates", ct);
        var needsBigDoubleText = columns.Contains("Cash") && !columns.Contains("CashText");

        List<string> statements = needsBigDoubleText ? [.. LegacyRealToBigDoubleText] : [];
        statements.Add(CreatePlayerSettings);

        if (needsBigDoubleText) logger.LogInformation("Migrating GameStates table to BigDouble TEXT schema");

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            foreach (var sql in statements)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Schema upgrade failed; previous schema retained");
            throw;
        }
    }

    public static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    public static async Task<HashSet<string>> GetColumnNamesAsync(SqliteConnection connection, string table, CancellationToken ct)
    {
        HashSet<string> columns = [with(StringComparer.OrdinalIgnoreCase)];
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }
}
