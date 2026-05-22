using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyAdventure.Shared.Services;

public partial class ToastItem(string message, DateTime expiresAt) : ObservableObject
{
    public string Message { get; } = message;
    public DateTime ExpiresAt { get; } = expiresAt;
}

/// <summary>
/// Auto-dismissing toast notifications. Views bind to <see cref="ActiveToasts"/>
/// and call <see cref="CleanupExpired"/> from their tick loop to drop
/// toasts whose lifetime has elapsed.
/// </summary>
public class ToastService
{
    public ObservableCollection<ToastItem> ActiveToasts { get; } = [];

    /// <summary>
    /// Show a toast with a default lifetime of 3 seconds. Caller can
    /// override the lifetime — passing <see cref="TimeSpan.Zero"/> makes
    /// the toast eligible for immediate cleanup (used by tests).
    /// </summary>
    public void Show(string message, TimeSpan? lifetime = null)
    {
        var life = lifetime ?? TimeSpan.FromSeconds(3);
        ActiveToasts.Add(new ToastItem(message, DateTime.UtcNow + life));
    }

    /// <summary>Remove any toast whose expiry time has passed.</summary>
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
