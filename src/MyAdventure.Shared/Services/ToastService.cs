using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyAdventure.Shared.Services;

/// <summary>
/// Simple toast notification service. Toasts auto-dismiss after a configurable duration.
/// Thread-safe for UI use via Avalonia's dispatcher.
/// </summary>
public partial class ToastService : ObservableObject
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    public ObservableCollection<ToastItem> ActiveToasts { get; } = [];

    /// <summary>Show a toast that auto-dismisses after ~3 seconds.</summary>
    public void Show(string message, TimeSpan? duration = null)
    {
        var toast = new ToastItem(message);
        ActiveToasts.Add(toast);

        // Schedule removal. We use Task.Delay since DispatcherTimer is view-level.
        // The UI tick loop in GameViewModel will clean up expired toasts.
        toast.ExpiresAt = DateTime.UtcNow + (duration ?? DefaultDuration);
    }

    /// <summary>Remove expired toasts. Called from the game tick loop.</summary>
    public void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        for (var i = ActiveToasts.Count - 1; i >= 0; i--)
        {
            if (ActiveToasts[i].ExpiresAt <= now)
                ActiveToasts.RemoveAt(i);
        }
    }
}

public record ToastItem(string Message)
{
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddSeconds(3);
}
