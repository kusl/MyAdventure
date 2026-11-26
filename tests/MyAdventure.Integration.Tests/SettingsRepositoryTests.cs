using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using Shouldly;

namespace MyAdventure.Integration.Tests;

public sealed class SettingsRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_NothingSaved_ReturnsDefaults()
    {
        var settings = await _database.Get<ISettingsRepository>().LoadAsync(Ct);

        settings.ShouldBe(PlayerSettings.Default);
        settings.SoundEnabled.ShouldBeTrue();
        settings.HapticsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var repo = _database.Get<ISettingsRepository>();

        await repo.SaveAsync(new PlayerSettings(SoundEnabled: false, HapticsEnabled: true), Ct);
        await repo.SaveAsync(new PlayerSettings(SoundEnabled: false, HapticsEnabled: false), Ct);

        (await repo.LoadAsync(Ct)).ShouldBe(new PlayerSettings(false, false));
    }

    public void Dispose() => _database.Dispose();
}
