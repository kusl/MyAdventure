using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Shared.Feedback;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class GameViewModelSettingsTests
{
    private readonly ISoundPlayer _sound = Substitute.For<ISoundPlayer>();
    private readonly FeedbackService _feedback;
    private readonly InMemorySettingsRepository _settings = new(new PlayerSettings(SoundEnabled: false, HapticsEnabled: true));

    public GameViewModelSettingsTests() =>
        _feedback = new FeedbackService(_sound, new NoHaptics(), NullLogger<FeedbackService>.Instance);

    [Fact]
    public async Task Initialize_AppliesSavedSettings()
    {
        var vm = Ui.CreateGame(Ui.CreateEngine(), feedback: _feedback, settings: _settings);

        await vm.InitializeAsync();

        vm.SoundEnabled.ShouldBeFalse();
        vm.HapticsEnabled.ShouldBeTrue();
        vm.HapticsSupported.ShouldBeFalse();
        _feedback.SoundEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        var engine = Ui.CreateEngine();
        var vm = Ui.CreateGame(engine, feedback: _feedback, settings: _settings);

        await vm.InitializeAsync();
        engine.Cash = new BigDouble(123);
        await vm.InitializeAsync();

        engine.Cash.ShouldBe(new BigDouble(123));
        vm.Businesses.Count.ShouldBe(6);
    }

    [Fact]
    public async Task ToggleSound_PersistsAndPlaysConfirmation()
    {
        var vm = Ui.CreateGame(Ui.CreateEngine(), feedback: _feedback, settings: _settings);
        await vm.InitializeAsync();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleSoundCommand.ExecuteAsync(null);

        vm.SoundEnabled.ShouldBeTrue();
        changed.ShouldContain(nameof(vm.SoundEnabled));
        _settings.Current.ShouldBe(new PlayerSettings(SoundEnabled: true, HapticsEnabled: true));
        _sound.Received(1).Play(FeedbackCue.Tap);
    }

    [Fact]
    public async Task Prestige_Unavailable_PlaysDenied()
    {
        var feedback = Substitute.For<IFeedbackService>();
        var vm = Ui.CreateGame(Ui.CreateEngine(), feedback: feedback);

        await vm.PrestigeCommand.ExecuteAsync(null);

        feedback.Received(1).Play(FeedbackCue.Denied);
    }

    [Fact]
    public async Task Prestige_Available_PlaysPrestige()
    {
        var feedback = Substitute.For<IFeedbackService>();
        var engine = Ui.CreateEngine();
        engine.LifetimeEarnings = new BigDouble(1e16);
        var vm = Ui.CreateGame(engine, feedback: feedback);
        vm.OnTick();

        await vm.PrestigeCommand.ExecuteAsync(null);

        feedback.Received(1).Play(FeedbackCue.Prestige);
        engine.PrestigeCount.ShouldBe(1);
    }
}
