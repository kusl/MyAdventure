using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Saves;
using MyAdventure.Infrastructure.Data;
using Shouldly;

namespace MyAdventure.Integration.Tests;

public sealed class GameStateRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly IGameStateRepository _repo;

    public GameStateRepositoryTests() => _repo = _database.Get<IGameStateRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_EmptyDatabase_ReturnsNull()
    {
        (await _repo.LoadAsync(Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip()
    {
        var lastPlayed = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new GameSnapshot
        {
            Cash = BigDouble.Parse("1.23456e3"),
            LifetimeEarnings = BigDouble.Parse("9.99999e3"),
            AngelInvestors = new BigDouble(42),
            PrestigeCount = 3,
            Owned = new Dictionary<string, long> { ["lemonade"] = 5 },
            Managers = new Dictionary<string, bool> { ["lemonade"] = true },
            LastPlayedAt = lastPlayed,
        };

        await _repo.SaveAsync(snapshot, Ct);
        var loaded = await _repo.LoadAsync(Ct);

        loaded.ShouldNotBeNull();
        loaded.Cash.ShouldBe(snapshot.Cash);
        loaded.LifetimeEarnings.ShouldBe(snapshot.LifetimeEarnings);
        loaded.AngelInvestors.ShouldBe(snapshot.AngelInvestors);
        loaded.PrestigeCount.ShouldBe(3);
        loaded.Owned["lemonade"].ShouldBe(5);
        loaded.Managers["lemonade"].ShouldBeTrue();
        loaded.LastPlayedAt.ShouldBe(lastPlayed);
        loaded.LastPlayedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Save_Twice_ShouldUpdateSingleRow()
    {
        await _repo.SaveAsync(new GameSnapshot { Cash = new BigDouble(100) }, Ct);
        await _repo.SaveAsync(new GameSnapshot { Cash = new BigDouble(200) }, Ct);

        (await _repo.LoadAsync(Ct))!.Cash.ShouldBe(new BigDouble(200));

        await using var db = await _database.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>().CreateDbContextAsync(Ct);
        db.GameStates.Count().ShouldBe(1);
    }

    [Fact]
    public async Task DeleteAll_ShouldClearState()
    {
        await _repo.SaveAsync(new GameSnapshot { Cash = new BigDouble(100) }, Ct);
        await _repo.DeleteAllAsync(Ct);

        (await _repo.LoadAsync(Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_ExtremeBigDouble_RoundTripsExactly()
    {
        await _repo.SaveAsync(new GameSnapshot
        {
            Cash = BigDouble.Parse("1.5e500"),
            LifetimeEarnings = BigDouble.Parse("7.25e1000"),
            AngelInvestors = BigDouble.Parse("2.5e15"),
        }, Ct);

        var loaded = await _repo.LoadAsync(Ct);

        loaded.ShouldNotBeNull();
        loaded.Cash.ToCanonicalString().ShouldBe("1.5e500");
        loaded.LifetimeEarnings.ToCanonicalString().ShouldBe("7.25e1000");
        loaded.AngelInvestors.ToCanonicalString().ShouldBe("2.5e15");
    }

    [Fact]
    public async Task Load_CorruptColumns_FallsBackToSafeDefaults()
    {
        await _repo.SaveAsync(new GameSnapshot { Cash = new BigDouble(1) }, Ct);
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE GameStates SET CashText = 'garbage', BusinessDataJson = 'not json'";
            await command.ExecuteNonQueryAsync(Ct);
        }

        var loaded = await _repo.LoadAsync(Ct);

        loaded.ShouldNotBeNull();
        loaded.Cash.IsZero.ShouldBeTrue();
        loaded.Owned.ShouldBeEmpty();
    }

    public void Dispose() => _database.Dispose();
}
