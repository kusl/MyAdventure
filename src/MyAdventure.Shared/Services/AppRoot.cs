using Avalonia;

namespace MyAdventure.Shared.Services;

/// <summary>
/// Holds the currently-active top-level Visual so platform services
/// (clipboard, etc.) can find it without per-platform application-lifetime
/// branching. Views register themselves on attach and clear on detach.
///
/// <para>
/// This pattern replaces the per-platform branching that used to be
/// needed in Avalonia 11 (where ISingleViewApplicationLifetime exposed
/// MainView and IClassicDesktopStyleApplicationLifetime exposed
/// MainWindow). In v12 Android no longer exposes a live MainView
/// (only a MainViewFactory), so the View-publishes-itself approach is
/// the cleanest cross-platform solution.
/// </para>
/// </summary>
public static class AppRoot
{
    /// <summary>
    /// The currently active root Visual, or null if none is attached.
    /// Set by views on attach (MainWindow.OnOpened, MainView.OnAttachedToVisualTree)
    /// and cleared on detach.
    /// </summary>
    public static Visual? CurrentVisual { get; set; }
}
