using Microsoft.Extensions.Logging;

namespace MyAdventure.Shared.Feedback;

public sealed class FeedbackService(ISoundPlayer sound, IHapticPlayer haptics, ILogger<FeedbackService> logger) : IFeedbackService
{
    public bool SoundEnabled { get; set; } = true;

    public bool HapticsEnabled { get; set; } = true;

    public bool HapticsSupported => haptics.IsSupported;

    public void Play(FeedbackCue cue)
    {
        try
        {
            if (SoundEnabled) sound.Play(cue);
            if (HapticsEnabled && haptics.IsSupported) haptics.Play(cue);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Feedback cue {Cue} failed", cue);
        }
    }
}
