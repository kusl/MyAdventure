using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MyAdventure.Shared.Feedback;

namespace MyAdventure.Desktop;

public sealed partial class DesktopSoundPlayer(ILogger<DesktopSoundPlayer> logger) : ISoundPlayer
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x0002_0000;

    private static readonly long MinimumGap = Stopwatch.Frequency / 25;
    private static readonly PlayerCommand[] UnixPlayers = [new("pw-play", []), new("paplay", []), new("aplay", ["-q"])];

    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyAdventure", "sfx");
    private readonly HashSet<FeedbackCue> _rendered = [];
    private readonly PlayerCommand? _command = ResolveCommand();
    private long _lastPlayed;
    private bool _disabled;

    public void Play(FeedbackCue cue)
    {
        var now = Stopwatch.GetTimestamp();
        if (_disabled || now - _lastPlayed < MinimumGap) return;
        _lastPlayed = now;

        try
        {
            var path = Render(cue);
            if (OperatingSystem.IsWindows())
            {
                PlaySound(path, 0, SndFilename | SndAsync | SndNoDefault);
            }
            else if (_command is { } command)
            {
                Start(command, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            _disabled = true;
            logger.LogWarning(ex, "Sound playback disabled for this session");
        }
    }

    private string Render(FeedbackCue cue)
    {
        var path = Path.Combine(_directory, $"{cue}.wav");
        if (_rendered.Add(cue))
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(path, ToneSynthesizer.Render(cue));
        }
        return path;
    }

    private static void Start(PlayerCommand command, string path)
    {
        var info = new ProcessStartInfo(command.Executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in command.Arguments) info.ArgumentList.Add(argument);
        info.ArgumentList.Add(path);

        if (Process.Start(info) is { } process) _ = ReapAsync(process);
    }

    private static async Task ReapAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    private static PlayerCommand? ResolveCommand()
    {
        if (OperatingSystem.IsMacOS()) return new("afplay", []);
        if (OperatingSystem.IsWindows()) return null;

        var searchPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var player in UnixPlayers)
        {
            if (searchPath.Select(dir => Path.Combine(dir, player.Executable)).FirstOrDefault(File.Exists) is { } executable)
            {
                return player with { Executable = executable };
            }
        }

        return null;
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string? sound, nint module, uint flags);

    private sealed record PlayerCommand(string Executable, string[] Arguments);
}
