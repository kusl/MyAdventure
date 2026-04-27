using Avalonia;

namespace MyAdventure.Shared.Services;

/// <summary>
/// Holds a reference to the currently-attached top-level visual.
/// Set by the active View (<c>MainWindow</c> on desktop, <c>MainView</c> on
/// Android) when it attaches to the visual tree, and cleared when it
/// detaches. Read by services that need a <c>TopLevel</c> to access
/// platform features such as the clipboard, storage provider, or screens.
///
/// <para>
/// The reason this exists as a static rather than a constructor-injected
/// service: in Avalonia 12, Android's <c>IActivityApplicationLifetime</c>
/// only exposes a <c>MainViewFactory</c> (a <c>Func&lt;Control&gt;</c>),
/// not a live view reference. Activities can be recreated, so the live
/// view changes over time. The cleanest cross-platform way to bridge the
/// gap is to have the View itself publish "I am the current top-level"
/// when it attaches. ViewModels that need a clipboard then read this and
/// call <c>TopLevel.GetTopLevel(visual)?.Clipboard</c> — works the same
/// on desktop, Android, iOS, and browser, with no per-platform branching.
/// </para>
///
/// <para>
/// This is deliberately a single-active-visual model: at any point in time
/// there is one foreground view in MyAdventure. If the project ever grows
/// to multi-window scenarios, this should evolve into a stack or a service
/// that picks the right TopLevel for the call site.
/// </para>
/// </summary>
public static class AppRoot
{
    /// <summary>
    /// The currently-attached visual. Pass to <c>TopLevel.GetTopLevel(...)</c>
    /// to obtain platform features. May be <c>null</c> during startup before
    /// the view attaches, or briefly during activity recreation on Android.
    /// </summary>
    public static Visual? CurrentVisual { get; set; }
}
