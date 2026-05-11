using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

/// <summary>
/// Main game ViewModel. Drives the game loop and exposes all state for binding.
/// </summary>
public partial class GameViewModel : ViewModelBase
{
    private readonly GameEngine _engine;
    private readonly ILogger<GameViewModel> _logger;
    private readonly ToastService _toasts;
    private readonly TimeProvider _time;
    private DateTime _lastTick;
    private int _saveCounter;

    /// <summary>
    /// UTC timestamp captured when the OS notifies us that the app is going
    /// to background (<see cref="OnSuspended"/>). <c>null</c> means we have
    /// not been suspended in the current process lifetime — important
    /// because cold start runs <see cref="GameEngine.LoadAsync"/>, which
    /// already covers the offline gap, and we must not also run
    /// <see cref="OnResumed"/>'s gap calculation in that case (double-pay).
    /// </summary>
    private DateTime? _suspendedAt;

    [ObservableProperty] private string _cashText = "$0.00";
    [ObservableProperty] private string _angelText = "0";
    [ObservableProperty] private string _angelBonusText = "\u00D71"; // "×1"
    [ObservableProperty] private int _prestigeCount;
    [ObservableProperty] private bool _canPrestige;
    [ObservableProperty] private string _nextAngelText = "0";
    [ObservableProperty] private string _prestigeExplanation = "";

    // --- Transfer panel (import/export) ---
    [ObservableProperty] private bool _isTransferOpen;
    [ObservableProperty] private bool _isExportMode;
    [ObservableProperty] private string _transferText = "";

    public ObservableCollection<BusinessViewModel> Businesses { get; } = [];
    public ToastService Toasts => _toasts;

    public GameViewModel(GameEngine engine, ILogger<GameViewModel> logger, ToastService toasts)
        : this(engine, logger, toasts, TimeProvider.System)
    {
    }

    /// <summary>
    /// Test-friendly overload that accepts a <see cref="TimeProvider"/>
    /// so unit tests can drive <see cref="OnSuspended"/> / <see cref="OnResumed"/>
    /// against a controllable clock without sleeping the test thread.
    /// </summary>
    public GameViewModel(GameEngine engine, ILogger<GameViewModel> logger, ToastService toasts, TimeProvider timeProvider)
    {
        _engine = engine;
        _logger = logger;
        _toasts = toasts;
        _time = timeProvider;
        _lastTick = _time.GetUtcNow().UtcDateTime;
    }

    public async Task InitializeAsync()
    {
        await _engine.LoadAsync();

        Businesses.Clear();
        foreach (var biz in _engine.Businesses)
            Businesses.Add(new BusinessViewModel(biz, _engine, _toasts));

        RefreshAll();
        _logger.LogInformation("Game initialized with {Count} businesses", Businesses.Count);
    }

    /// <summary>Called by the UI timer (~60fps).</summary>
    public void OnTick()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Defensive cap. This protects cold start (where _lastTick is
        // initialized in the constructor but the first OnTick may be
        // many seconds later) and debugger pauses. The suspend/resume
        // gap is handled by OnSuspended/OnResumed, NOT by relaxing this
        // cap — silent multi-minute deltas would otherwise produce
        // confusing UI behavior on the first post-resume tick.
        delta = Math.Min(delta, 1.0);

        _engine.Tick(delta);
        RefreshAll();

        // Clean up expired toasts
        _toasts.CleanupExpired();

        // Auto-save every ~5 seconds
        _saveCounter++;
        if (_saveCounter >= 300)
        {
            _saveCounter = 0;
            _ = SaveAsync();
        }
    }

    /// <summary>
    /// Called by <see cref="AppLifecycleManager"/> when the OS notifies the
    /// app it is entering the background (Android backgrounded, desktop
    /// hibernate/sleep). Stamps the suspension time so we can compute the
    /// gap on resume, and triggers an immediate save so the persisted
    /// <c>LastPlayedAt</c> is as fresh as possible in case the OS later
    /// kills the process without ever resuming us.
    ///
    /// <para>
    /// Idempotent: calling twice in a row simply re-stamps the timestamp,
    /// which is fine — the only thing that matters is that
    /// <see cref="OnResumed"/> sees a value to subtract from "now."
    /// </para>
    /// </summary>
    public void OnSuspended()
    {
        _suspendedAt = _time.GetUtcNow().UtcDateTime;
        _logger.LogInformation("App suspended at {SuspendedAt:o}", _suspendedAt);

        // Fire-and-forget save: the OS gives us limited time after going to
        // background, and we already use the same fire-and-forget pattern
        // in OnTick's auto-save. If the save loses to the suspend, the
        // cold-load offline-earnings path will still pick up the gap from
        // the last successful auto-save.
        _ = SaveAsync();
    }

    /// <summary>
    /// Called by <see cref="AppLifecycleManager"/> when the OS notifies the
    /// app it is returning to the foreground from a background suspension.
    /// Computes the time gap since <see cref="OnSuspended"/>, applies
    /// offline earnings for that gap, resets the tick timestamp so the
    /// next tick computes a small natural delta, and shows the player a
    /// "while you were away" toast. Then forces a UI refresh.
    ///
    /// <para>
    /// <b>Guard against double-counting:</b> if <see cref="_suspendedAt"/>
    /// is <c>null</c> we skip the gap calculation entirely. Cold start
    /// already runs <see cref="GameEngine.LoadAsync"/>, which has its own
    /// offline-earnings path, and we must not run both.
    /// </para>
    /// </summary>
    public void OnResumed()
    {
        // Snapshot _suspendedAt and clear it BEFORE doing anything else,
        // so a re-entrant call (rare, but observed on some platforms that
        // fire duplicate Activated events) can't double-pay. The cold-start
        // guard then degenerates to "is the snapshot null?".
        var suspended = _suspendedAt;
        _suspendedAt = null;

        if (suspended is null)
        {
            // No prior OnSuspended in this process lifetime — this is
            // either cold start or a spurious activation event. Either
            // way: nothing to compensate for, just resync _lastTick so
            // the next tick is sane.
            _lastTick = _time.GetUtcNow().UtcDateTime;
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var elapsed = now - suspended.Value;

        var earned = _engine.ApplyOfflineEarnings(elapsed);

        // Reset _lastTick AFTER applying offline earnings: otherwise the
        // first post-resume tick would still see a multi-minute delta
        // (clamped to 1s) on top of what we just applied — a small but
        // unnecessary double-count.
        _lastTick = now;

        if (earned > 0)
        {
            _logger.LogInformation("Applied resume earnings: {Earned:F2} for {Seconds:F0}s suspended",
                earned, elapsed.TotalSeconds);
            _toasts.Show($"While you were away, you earned ${NumberFormatter.Format(earned)}!");
        }
        else
        {
            _logger.LogDebug("Resume gap was {Seconds:F2}s; no offline earnings applied",
                elapsed.TotalSeconds);
        }

        RefreshAll();
    }

    [RelayCommand]
    private void Export()
    {
        // Wrap in try-catch as a final safety net. The engine itself now
        // sanitizes non-finite values before JSON serialization, so this
        // catch should never fire — but if a future change somehow
        // re-introduces a non-finite-in-state bug, the player gets a
        // toast instead of a force-close.
        try
        {
            TransferText = _engine.ExportToString();
            IsExportMode = true;
            IsTransferOpen = true;
            _logger.LogInformation("Exported game state ({Length} chars)", TransferText.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export game state");
            _toasts.Show("Could not export — please report this as a bug.");
        }
    }

    [RelayCommand]
    private async Task CopyExportAsync()
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(TransferText);
                _toasts.Show("Copied to clipboard!");
            }
            else
            {
                _toasts.Show("Clipboard not available on this platform. Select all and copy manually.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy to clipboard");
            _toasts.Show("Could not copy — select the text and copy manually.");
        }
    }

    [RelayCommand]
    private void StartImport()
    {
        TransferText = "";
        IsExportMode = false;
        IsTransferOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (string.IsNullOrWhiteSpace(TransferText))
        {
            _toasts.Show("Paste an export string first!");
            return;
        }

        if (_engine.ImportFromString(TransferText))
        {
            // Rebuild business view models from the newly imported state
            Businesses.Clear();
            foreach (var biz in _engine.Businesses)
                Businesses.Add(new BusinessViewModel(biz, _engine, _toasts));

            RefreshAll();
            await SaveAsync();

            IsTransferOpen = false;
            TransferText = "";
            _toasts.Show("Progress imported successfully!");
            _logger.LogInformation("Game state imported and saved");
        }
        else
        {
            _toasts.Show("Invalid import string. Check and try again.");
        }
    }

    [RelayCommand]
    private void CloseTransfer()
    {
        IsTransferOpen = false;
        TransferText = "";
    }

    [RelayCommand]
    private async Task PrestigeAsync()
    {
        if (!CanPrestige)
        {
            _toasts.Show(
                "Prestige resets all businesses and cash, but you gain Angel Investors " +
                "that permanently boost all revenue by +2% each. " +
                $"You need to earn more to unlock prestige (earn enough for at least 1 angel).");
            return;
        }

        var potentialAngels = GameEngine.CalculateAngels(_engine.LifetimeEarnings) - _engine.AngelInvestors;
        var (angels, success) = _engine.Prestige();
        if (!success) return;

        _logger.LogInformation("Prestige! Gained {Angels:F0} angels", angels);

        Businesses.Clear();
        foreach (var biz in _engine.Businesses)
            Businesses.Add(new BusinessViewModel(biz, _engine, _toasts));

        RefreshAll();
        await SaveAsync();

        _toasts.Show($"Prestige! Gained {NumberFormatter.Format(angels)} angels. All revenue boosted!");
    }

    public async Task SaveAsync()
    {
        try
        {
            await _engine.SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save game");
        }
    }

    private void RefreshAll()
    {
        CashText = $"${NumberFormatter.Format(_engine.Cash)}";
        AngelText = NumberFormatter.Format(_engine.AngelInvestors);

        // Display the angel bonus as a multiplier ("×N") rather than a
        // percentage. The previous "+(bonus-1)*100%" formulation broke
        // visually past ~50 angels (showing "+200,000%" was already
        // illegible) and broke arithmetically past ~35,750 angels, where
        // the subtraction Infinity - 1 produces NaN and propagates into
        // the UI as "+NaN%". A multiplier reads the same at every scale:
        // ×2.69, ×52, ×1.00 K, all the way up to "×∞" only if the engine
        // ever lets the bonus go non-finite (it doesn't — see the cap on
        // GameEngine.AngelBonus).
        AngelBonusText = $"\u00D7{NumberFormatter.Format(_engine.AngelBonus)}";

        PrestigeCount = _engine.PrestigeCount;

        var potentialAngels = GameEngine.CalculateAngels(_engine.LifetimeEarnings) - _engine.AngelInvestors;
        CanPrestige = potentialAngels >= 1;
        NextAngelText = NumberFormatter.Format(Math.Max(0, potentialAngels));

        // Prestige explanation that auto-updates
        if (CanPrestige)
        {
            PrestigeExplanation = $"Reset all businesses. Gain {NextAngelText} angels (+2% revenue each).";
        }
        else
        {
            PrestigeExplanation = "Keep earning! Need enough lifetime earnings to gain at least 1 angel.";
        }

        // Snapshot AngelBonus once and pass to every business so all cards
        // display consistent post-bonus revenue figures within a single tick.
        var angelBonus = _engine.AngelBonus;
        foreach (var bvm in Businesses)
            bvm.Refresh(_engine.Cash, angelBonus);
    }

    /// <summary>
    /// Get the clipboard from the active top-level.
    /// Works on desktop, Android, iOS, and browser uniformly: the active
    /// View registers itself with <see cref="AppRoot.CurrentVisual"/> when
    /// it attaches to the visual tree, and we ask it for its TopLevel.
    /// <para>
    /// This pattern replaces the per-platform application-lifetime branching
    /// that was needed in Avalonia 11. In v12 Android no longer exposes a
    /// live <c>MainView</c> via <see cref="Avalonia.Controls.ApplicationLifetimes.IActivityApplicationLifetime"/>
    /// (only a factory), so the View-publishes-itself approach is the only
    /// clean cross-platform solution that doesn't reach into platform-specific
    /// types like <c>AndroidActivatableLifetime.CurrentMainActivity</c>.
    /// </para>
    /// </summary>
    private static IClipboard? GetClipboard()
    {
        try
        {
            var visual = AppRoot.CurrentVisual;
            if (visual is null) return null;
            return TopLevel.GetTopLevel(visual)?.Clipboard;
        }
        catch
        {
            return null;
        }
    }
}
