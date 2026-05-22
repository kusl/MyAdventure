using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using Shouldly;

namespace MyAdventure.Integration.Tests;

/// <summary>
/// Integration tests for the SQLite-backed game state repository.
/// Post-BigDouble migration: the numeric columns are TEXT holding canonical
/// BigDouble strings, so round-trips must preserve string content exactly.
/// </summary>
public class GameStateRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GameStateRepository _repo;

    public GameStateRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new GameStateRepository(_db, NullLogger<GameStateRepository>.Instance);
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip()
    {
        var state = new GameState
        {
            CashText = "1.23456e3",
            LifetimeEarningsText = "9.99999e3",
            BusinessDataJson = """{"lemonade":5}""",
            ManagerDataJson = """{"lemonade":true}"""
        };

        await _repo.SaveAsync(state);
        var loaded = await _repo.GetLatestAsync();

        loaded.ShouldNotBeNull();
        loaded.CashText.ShouldBe("1.23456e3");
        loaded.LifetimeEarningsText.ShouldBe("9.99999e3");
        loaded.BusinessDataJson.ShouldContain("lemonade");
    }

    [Fact]
    public async Task Save_Twice_ShouldUpdate()
    {
        await _repo.SaveAsync(new GameState { CashText = "100" });
        await _repo.SaveAsync(new GameState { CashText = "200" });

        var loaded = await _repo.GetLatestAsync();
        loaded!.CashText.ShouldBe("200");
        _db.GameStates.Count().ShouldBe(1);
    }

    [Fact]
    public async Task DeleteAll_ShouldClearState()
    {
        await _repo.SaveAsync(new GameState { CashText = "100" });
        await _repo.DeleteAllAsync();

        var loaded = await _repo.GetLatestAsync();
        loaded.ShouldBeNull();
    }

    /// <summary>
    /// BigDouble migration regression: ensure that an extreme BigDouble
    /// string (e.g. cash = 1e500, far past what double can represent)
    /// round-trips through SQLite without precision loss.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_ExtremeBigDoubleString_RoundTripsExactly()
    {
        var state = new GameState
        {
            CashText = "1.5e500",
            LifetimeEarningsText = "7.25e1000",
            AngelInvestorsText = "2.5e15"
        };

        await _repo.SaveAsync(state);
        var loaded = await _repo.GetLatestAsync();

        loaded.ShouldNotBeNull();
        loaded.CashText.ShouldBe("1.5e500");
        loaded.LifetimeEarningsText.ShouldBe("7.25e1000");
        loaded.AngelInvestorsText.ShouldBe("2.5e15");
    }

    public void Dispose() => _db.Dispose();
}
