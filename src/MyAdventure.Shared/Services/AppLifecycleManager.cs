using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Shared.Services;

/// <summary>
/// Cross-platform glue between Avalonia's <see cref="IActivatableLifetime"/>
/// and the game's suspend/resume hooks on <see cref="GameViewModel"/>.
///
/// <para>
/// Holds a single replaceable static "current target" so Android activity
/// recreation (which calls <see cref="Attach"/> repeatedly with a fresh
/// ViewModel each time) doesn't leak event handlers on the old VM.
/// </para>
/// </summary>
public static class AppLifecycleManager
{
    private static GameViewModel? _currentTarget;
    private static bool _isAttached;

    /// <summary>
    /// Wire the given <paramref name="viewModel"/> to OS lifecycle events.
    /// Replaces any previous target so older VMs stop receiving events.
    /// Returns true if a lifetime feature was found, false otherwise
    /// (e.g. when running in headless tests where no Avalonia Application
    /// is initialized).
    /// </summary>
    public static bool Attach(GameViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _currentTarget = viewModel;

        if (_isAttached) return true;

        var feature = Application.Current?.TryGetFeature<IActivatableLifetime>();
        if (feature is null) return false;

        feature.Activated += OnActivated;
        feature.Deactivated += OnDeactivated;
        _isAttached = true;
        return true;
    }

    /// <summary>
    /// Test-only seam to reset the static "current target" between cases
    /// so tests don't observe each other's state. Not for production use.
    /// </summary>
    internal static void ResetForTesting()
    {
        _currentTarget = null;
        _isAttached = false;
    }

    private static void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background) return;
        _currentTarget?.OnResumed();
    }

    private static void OnDeactivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background) return;
        _currentTarget?.OnSuspended();
    }
}
