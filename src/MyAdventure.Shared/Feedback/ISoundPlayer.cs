namespace MyAdventure.Shared.Feedback;

public interface ISoundPlayer
{
    void Play(FeedbackCue cue);
}

public sealed class SilentSoundPlayer : ISoundPlayer
{
    public void Play(FeedbackCue cue)
    {
    }
}
