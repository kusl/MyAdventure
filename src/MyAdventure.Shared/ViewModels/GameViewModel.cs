using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

public partial class GameViewModel : ViewModelBase
{
    private readonly GameEngine _engine;
    private readonly ILogger<GameViewModel> _logger;
    private readonly ToastService _toasts;
    private readonly TimeProvider _time;
    private DateTime _lastTick;
    private int _saveCounter;

    private DateTime? _suspendedAt;

    [ObservableProperty] private string _cashText = "$0.00";
    [ObservableProperty] private string _angelText = "0";
    [ObservableProperty] private string _angelBonusText = "\u00D71";
    [ObservableProperty] private int _prestigeCount;
    [ObservableProperty] private bool _canPrestige;
    [ObservableProperty] private string _nextAngelText = "0";
    [ObservableProperty] private string _prestigeExplanation = "";

    [ObservableProperty] private string _crossSpeedText = "\u00D71 Speed";

    [ObservableProperty] private bool _hasCrossSpeedBonus;

    [ObservableProperty] private string _nextCrossThresholdText = "";

    [ObservableProperty] private string _minOwnedAcrossText = "0";

    [ObservableProperty] private bool _isTransferOpen;
    [ObservableProperty] private bool _isExportMode;
    [ObservableProperty] private string _transferText = "";

    public ObservableCollection<BusinessViewModel> Businesses { get; } = [];
    public ToastService Toasts => _toasts;

    public GameViewModel(GameEngine engine, ILogger<GameViewModel> logger, ToastService toasts)
        : this(engine, logger, toasts, TimeProvider.System)
    {
    }

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

    public void OnTick()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        delta = Math.Min(delta, 1.0);

        _engine.Tick(delta);
        RefreshAll();

        _toasts.CleanupExpired();

        _saveCounter++;
        if (_saveCounter >= 300)
        {
            _saveCounter = 0;
            _ = SaveAsync();
        }
    }

    public void OnSuspended()
    {
        _suspendedAt = _time.GetUtcNow().UtcDateTime;
        _logger.LogInformation("App suspended at {SuspendedAt:o}", _suspendedAt);

        _ = SaveAsync();
    }

    public void OnResumed()
    {
        var suspended = _suspendedAt;
        _suspendedAt = null;

        if (suspended is null)
        {
            _lastTick = _time.GetUtcNow().UtcDateTime;
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var elapsed = now - suspended.Value;

        var earned = _engine.ApplyOfflineEarnings(elapsed);

        _lastTick = now;

        if (earned.Sign > 0)
        {
            _logger.LogInformation("Applied resume earnings: {Earned} for {Seconds:F0}s suspended",
                earned.ToCanonicalString(), elapsed.TotalSeconds);
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

        var (angels, success) = _engine.Prestige();
        if (!success) return;

        _logger.LogInformation("Prestige! Gained {Angels} angels", angels.ToCanonicalString());

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

        AngelBonusText = $"\u00D7{NumberFormatter.Format(_engine.AngelBonus)}";

        PrestigeCount = _engine.PrestigeCount;

        var potentialAngels = _engine.PotentialAngels;
        CanPrestige = potentialAngels >= BigDouble.One;
        NextAngelText = potentialAngels.Sign > 0
            ? NumberFormatter.Format(potentialAngels)
            : "0";

        if (CanPrestige)
        {
            PrestigeExplanation = $"Reset all businesses. Gain {NextAngelText} angels: income \u00D7{NumberFormatter.Format(_engine.PrestigeIncomeMultiplier)}.";
        }
        else
        {
            PrestigeExplanation = "Keep earning! Need enough lifetime earnings to gain at least 1 angel.";
        }

        var angelBonus = _engine.AngelBonus;
        var crossBonus = _engine.CrossBusinessSpeedMultiplier;
        var cash = _engine.Cash;
        foreach (var bvm in Businesses)
            bvm.Refresh(cash, angelBonus, crossBonus);

        var minOwned = _engine.MinOwnedAcrossBusinesses;
        MinOwnedAcrossText = minOwned.ToString();

        HasCrossSpeedBonus = crossBonus > BigDouble.One;

        if (HasCrossSpeedBonus)
        {
            CrossSpeedText = $"\u00D7{NumberFormatter.Format(crossBonus)} Speed";
        }
        else
        {
            CrossSpeedText = "\u00D71 Speed";
        }

        var nextThreshold = CrossBusinessSpeedBonus.NextThreshold(minOwned);
        NextCrossThresholdText = nextThreshold is { } next
            ? $"{CrossBusinessSpeedBonus.UnitsToNext(minOwned)} more of every business \u2192 {next}"
            : "All cross-business bonuses earned";
    }

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
