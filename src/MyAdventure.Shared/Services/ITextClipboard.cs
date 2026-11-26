using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace MyAdventure.Shared.Services;

public interface ITextClipboard
{
    Task<bool> TrySetTextAsync(string text);
}

public sealed class TopLevelClipboard(Visual visual) : ITextClipboard
{
    public async Task<bool> TrySetTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(visual)?.Clipboard is not { } clipboard) return false;
        await clipboard.SetTextAsync(text);
        return true;
    }
}
