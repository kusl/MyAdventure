using System.Runtime.Versioning;
using Android.Content;
using Android.OS;
using MyAdventure.Shared.Feedback;

namespace MyAdventure.Android;

public sealed class AndroidHapticPlayer(Context context) : IHapticPlayer
{
    private readonly Vibrator? _vibrator = context.GetSystemService(Java.Lang.Class.FromType(typeof(Vibrator))) as Vibrator;

    public bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(26) && _vibrator is { HasVibrator: true };

    public void Play(FeedbackCue cue)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        if (_vibrator is null) return;

        _vibrator.Vibrate(Effect(cue)!);
    }

    [SupportedOSPlatform("android26.0")]
    private static VibrationEffect? Effect(FeedbackCue cue) => cue switch
    {
        FeedbackCue.Tap => VibrationEffect.CreateOneShot(12, VibrationEffect.DefaultAmplitude),
        FeedbackCue.Purchase => VibrationEffect.CreateOneShot(20, VibrationEffect.DefaultAmplitude),
        FeedbackCue.Manager => VibrationEffect.CreateWaveform([0, 20, 40, 20], -1),
        FeedbackCue.Milestone => VibrationEffect.CreateWaveform([0, 30, 50, 30, 50, 45], -1),
        FeedbackCue.Prestige => VibrationEffect.CreateWaveform([0, 60, 60, 120], -1),
        _ => VibrationEffect.CreateWaveform([0, 15, 70, 15], -1),
    };
}
