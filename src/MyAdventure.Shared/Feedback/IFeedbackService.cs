namespace MyAdventure.Shared.Feedback;

public interface IFeedbackService
{
    bool SoundEnabled { get; set; }

    bool HapticsEnabled { get; set; }

    bool HapticsSupported { get; }

    void Play(FeedbackCue cue);
}
