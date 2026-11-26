namespace MyAdventure.Shared.Feedback;

public interface IHapticPlayer
{
    bool IsSupported { get; }

    void Play(FeedbackCue cue);
}

public sealed class NoHaptics : IHapticPlayer
{
    public bool IsSupported => false;

    public void Play(FeedbackCue cue)
    {
    }
}
