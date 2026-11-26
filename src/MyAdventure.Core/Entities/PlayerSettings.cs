namespace MyAdventure.Core.Entities;

public sealed record PlayerSettings(bool SoundEnabled = true, bool HapticsEnabled = true)
{
    public static PlayerSettings Default { get; } = new();
}
