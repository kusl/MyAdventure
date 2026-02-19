using Microsoft.EntityFrameworkCore;
using MyAdventure.Core.Entities;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace MyAdventure.Integration.Tests;

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
            Cash = 1234.56,
            LifetimeEarnings = 9999.99,
            BusinessDataJson = """{"lemonade":5}""",
            ManagerDataJson = """{"lemonade":true}"""
        };

        await _repo.SaveAsync(state);
        var loaded = await _repo.GetLatestAsync();

        loaded.ShouldNotBeNull();
        loaded.Cash.ShouldBe(1234.56);
        loaded.LifetimeEarnings.ShouldBe(9999.99);
        loaded.BusinessDataJson.ShouldContain("lemonade");
    }

    [Fact]
    public async Task Save_Twice_ShouldUpdate()
    {
        await _repo.SaveAsync(new GameState { Cash = 100 });
        await _repo.SaveAsync(new GameState { Cash = 200 });

        var loaded = await _repo.GetLatestAsync();
        loaded!.Cash.ShouldBe(200);
        _db.GameStates.Count().ShouldBe(1);
    }

    [Fact]
    public async Task DeleteAll_ShouldClearState()
    {
        await _repo.SaveAsync(new GameState { Cash = 100 });
        await _repo.DeleteAllAsync();

        var loaded = await _repo.GetLatestAsync();
        loaded.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
