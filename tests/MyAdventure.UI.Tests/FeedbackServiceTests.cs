using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Shared.Feedback;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class FeedbackServiceTests
{
    private readonly ISoundPlayer _sound = Substitute.For<ISoundPlayer>();
    private readonly IHapticPlayer _haptics = Substitute.For<IHapticPlayer>();

    public FeedbackServiceTests() => _haptics.IsSupported.Returns(true);

    private FeedbackService CreateService() => new(_sound, _haptics, NullLogger<FeedbackService>.Instance);

    [Fact]
    public void Play_BothEnabled_PlaysSoundAndHaptics()
    {
        CreateService().Play(FeedbackCue.Purchase);

        _sound.Received(1).Play(FeedbackCue.Purchase);
        _haptics.Received(1).Play(FeedbackCue.Purchase);
    }

    [Fact]
    public void Play_SoundDisabled_OnlyPlaysHaptics()
    {
        var service = CreateService();
        service.SoundEnabled = false;

        service.Play(FeedbackCue.Tap);

        _sound.DidNotReceiveWithAnyArgs().Play(default);
        _haptics.Received(1).Play(FeedbackCue.Tap);
    }

    [Fact]
    public void Play_HapticsDisabled_OnlyPlaysSound()
    {
        var service = CreateService();
        service.HapticsEnabled = false;

        service.Play(FeedbackCue.Denied);

        _sound.Received(1).Play(FeedbackCue.Denied);
        _haptics.DidNotReceiveWithAnyArgs().Play(default);
    }

    [Fact]
    public void Play_HapticsUnsupported_SkipsHaptics()
    {
        _haptics.IsSupported.Returns(false);
        var service = CreateService();

        service.Play(FeedbackCue.Milestone);

        service.HapticsSupported.ShouldBeFalse();
        _haptics.DidNotReceiveWithAnyArgs().Play(default);
    }

    [Fact]
    public void Play_PlayerThrows_IsSwallowed()
    {
        _sound.When(s => s.Play(Arg.Any<FeedbackCue>())).Do(_ => throw new InvalidOperationException("no audio device"));

        Should.NotThrow(() => CreateService().Play(FeedbackCue.Tap));
    }
}
