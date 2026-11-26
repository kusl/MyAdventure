using System.Collections.ObjectModel;

namespace MyAdventure.Shared.Services;

public sealed record ToastItem(string Message, DateTimeOffset ExpiresAt);

public sealed class ToastService(TimeProvider time)
{
    public static TimeSpan DefaultLifetime { get; } = TimeSpan.FromSeconds(3);

    public ToastService() : this(TimeProvider.System)
    {
    }

    public ObservableCollection<ToastItem> ActiveToasts { get; } = [];

    public void Show(string message, TimeSpan? lifetime = null) =>
        ActiveToasts.Add(new ToastItem(message, time.GetUtcNow() + (lifetime ?? DefaultLifetime)));

    public void CleanupExpired()
    {
        var now = time.GetUtcNow();
        for (var i = ActiveToasts.Count - 1; i >= 0; i--)
        {
            if (ActiveToasts[i].ExpiresAt <= now) ActiveToasts.RemoveAt(i);
        }
    }
}
