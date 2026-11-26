namespace MyAdventure.Infrastructure.Data;

public sealed class PlayerSettingsRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool SoundEnabled { get; set; } = true;
    public bool HapticsEnabled { get; set; } = true;
}
