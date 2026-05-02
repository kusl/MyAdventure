using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Shared.Services;

/// <summary>
/// Wires the cross-platform <see cref="IActivatableLifetime"/> (current in
/// Avalonia 12) to the active <see cref="GameViewModel"/> so the game can
/// react to the OS suspending and resuming the app.
///
/// <para>
/// <b>Why this exists.</b>
/// The game's <c>DispatcherTimer</c> stops firing while the app is in the
/// background (Android backgrounded, desktop suspended/hibernated). When the
/// timer resumes, the very first tick sees a multi-minute <c>delta</c>,
/// which the engine clamps to 1 second to avoid pathological cold-start
/// behavior. Without a separate signal, the rest of the gap is silently
/// discarded — the player loses up to many minutes of earnings on every
/// app switch. This class delivers the missing signal: <c>Deactivated</c>
/// for "going to background" (save + stamp), <c>Activated</c> for "coming
/// back" (apply offline earnings for the gap).
/// </para>
///
/// <para>
/// <b>Why one cross-platform implementation.</b>
/// Avalonia 12's <see cref="IActivatableLifetime"/> is the same feature
/// on desktop, Android, iOS, and browser. Subscribing here once means
/// desktop and Android share a single code path — no per-platform
/// lifecycle wiring scattered across <c>MainWindow.OnOpened</c>,
/// <c>MainView.OnAttachedToVisualTree</c>, <c>Window.Activated</c>, etc.
/// If the suspend/resume logic ever needs to change, it changes in
/// exactly one place and immediately benefits every target.
/// </para>
///
/// <para>
/// <b>Why <see cref="ActivationKind.Background"/> only.</b>
/// The same events also fire for protocol activation (deep links),
/// reopen-from-dock, and other reasons. We filter to
/// <see cref="ActivationKind.Background"/> so unrelated activations don't
/// trigger an offline-earnings calculation.
/// </para>
///
/// <para>
/// <b>Why a static current-VM target instead of one subscription per VM.</b>
/// On Android, <c>MainViewFactory</c> can be invoked multiple times across
/// an app's lifetime (each activity recreation produces a fresh VM). If
/// every fresh VM added its own event handler, the lifetime would
/// accumulate handlers and old VMs would keep receiving events after their
/// view was destroyed — a memory and correctness leak. Instead, this class
/// subscribes once on first <see cref="Attach"/>, then forwards events to
/// whatever VM is currently registered. <see cref="Attach"/> simply
/// updates the target.
/// </para>
/// </summary>
public static class AppLifecycleManager
{
    private static readonly object Gate = new();
    private static GameViewModel? _current;
    private static bool _subscribed;

    /// <summary>
    /// Register the given <see cref="GameViewModel"/> as the active target
    /// for application lifecycle events. On first call, subscribes to the
    /// <see cref="IActivatableLifetime"/> if available. On subsequent calls
    /// (e.g. Android activity recreation produces a fresh VM), simply
    /// replaces the previously-registered VM — old VMs stop receiving
    /// events with no manual unsubscribe needed.
    /// </summary>
    /// <param name="viewModel">The game's main ViewModel.</param>
    /// <returns>
    /// <c>true</c> if the lifetime feature was found and we are now
    /// forwarding events to <paramref name="viewModel"/>; <c>false</c> if
    /// the platform doesn't expose <see cref="IActivatableLifetime"/>
    /// (e.g. headless tests). The caller does not need to special-case the
    /// false return — the rest of the app remains functional, just without
    /// background/foreground earnings compensation.
    /// </returns>
    public static bool Attach(GameViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        lock (Gate)
        {
            _current = viewModel;

            if (_subscribed) return true;

            var app = Application.Current;
            if (app is null) return false;

            var lifetime = app.TryGetFeature<IActivatableLifetime>();
            if (lifetime is null) return false;

            // Filter to Background-kind activations only. Protocol/Reopen/etc.
            // come through the same channels and must not be treated as
            // "the player came back from being away" — those don't pause
            // the tick loop, so there's no gap to compensate for.
            lifetime.Deactivated += OnLifetimeDeactivated;
            lifetime.Activated += OnLifetimeActivated;
            _subscribed = true;
            return true;
        }
    }

    /// <summary>
    /// Test seam: clear the current target and reset subscription state.
    /// Production code never calls this; tests use it to reset between
    /// cases without leaking state across them.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            _current = null;
            // Note: we deliberately do NOT detach the event handler from
            // any real IActivatableLifetime here — unit tests don't have
            // one. Resetting _subscribed lets a follow-up test re-Attach
            // and test the subscription path again if it ever needs to.
            _subscribed = false;
        }
    }

    private static void OnLifetimeDeactivated(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind != ActivationKind.Background) return;
        var target = Volatile.Read(ref _current);
        target?.OnSuspended();
    }

    private static void OnLifetimeActivated(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind != ActivationKind.Background) return;
        var target = Volatile.Read(ref _current);
        target?.OnResumed();
    }
}
