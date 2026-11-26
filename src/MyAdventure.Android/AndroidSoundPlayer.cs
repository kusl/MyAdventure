using Android.Content;
using Android.Media;
using MyAdventure.Shared.Feedback;

namespace MyAdventure.Android;

public sealed class AndroidSoundPlayer : ISoundPlayer, IDisposable
{
    private const float Volume = 0.7f;

    private readonly SoundPool _pool;
    private readonly Dictionary<FeedbackCue, int> _sounds = [];

    public AndroidSoundPlayer(Context context)
    {
        _pool = new SoundPool.Builder().SetMaxStreams(4)!.Build()!;

        var directory = Path.Combine(context.CacheDir!.AbsolutePath!, "sfx");
        Directory.CreateDirectory(directory);
        foreach (var cue in Enum.GetValues<FeedbackCue>())
        {
            var path = Path.Combine(directory, $"{cue}.wav");
            File.WriteAllBytes(path, ToneSynthesizer.Render(cue));
            _sounds[cue] = _pool.Load(path, 1);
        }
    }

    public void Play(FeedbackCue cue)
    {
        if (_sounds.TryGetValue(cue, out var sound)) _pool.Play(sound, Volume, Volume, 1, 0, 1f);
    }

    public void Dispose() => _pool.Release();
}
