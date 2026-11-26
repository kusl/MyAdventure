using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Feedback;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

public sealed partial class GameViewModel : ObservableObject, ILifecycleAware
{
    private const double MaxTickSeconds = 1.0;
    private const int TicksPerAutosave = 300;

    private readonly GameEngine _engine;
    private readonly ISettingsRepository _settings;
    private readonly IFeedbackService _feedback;
    private readonly ILogger<GameViewModel> _logger;
    private readonly TimeProvider _time;
    private Task? _initialization;
    private DateTimeOffset _lastTick;
    private DateTimeOffset? _suspendedAt;
    private int _ticksSinceSave;

    public GameViewModel(
        GameEngine engine,
        ToastService toasts,
        ISettingsRepository settings,
        IFeedbackService feedback,
        ILogger<GameViewModel> logger,
        TimeProvider time)
    {
        _engine = engine;
        _settings = settings;
        _feedback = feedback;
        _logger = logger;
        _time = time;
        Toasts = toasts;
        _lastTick = time.GetUtcNow();
    }

    public ObservableCollection<BusinessViewModel> Businesses { get; } = [];

    public ToastService Toasts { get; }

    public ITextClipboard? Clipboard { get; set; }

    public string CashText { get; private set => SetProperty(ref field, value); } = "$0.00";
    public string AngelText { get; private set => SetProperty(ref field, value); } = "0";
    public string AngelBonusText { get; private set => SetProperty(ref field, value); } = "×1";
    public int PrestigeCount { get; private set => SetProperty(ref field, value); }
    public bool CanPrestige { get; private set => SetProperty(ref field, value); }
    public string NextAngelText { get; private set => SetProperty(ref field, value); } = "0";
    public string PrestigeExplanation { get; private set => SetProperty(ref field, value); } = "";
    public string CrossSpeedText { get; private set => SetProperty(ref field, value); } = "×1 Speed";
    public bool HasCrossSpeedBonus { get; private set => SetProperty(ref field, value); }
    public string NextCrossThresholdText { get; private set => SetProperty(ref field, value); } = "";
    public string MinOwnedAcrossText { get; private set => SetProperty(ref field, value); } = "0";
    public bool IsTransferOpen { get; private set => SetProperty(ref field, value); }
    public bool IsExportMode { get; private set => SetProperty(ref field, value); }
    public string TransferText { get; set => SetProperty(ref field, value); } = "";

    public bool SoundEnabled
    {
        get => _feedback.SoundEnabled;
        private set
        {
            if (_feedback.SoundEnabled == value) return;
            _feedback.SoundEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool HapticsEnabled
    {
        get => _feedback.HapticsEnabled;
        private set
        {
            if (_feedback.HapticsEnabled == value) return;
            _feedback.HapticsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool HapticsSupported => _feedback.HapticsSupported;

    public Task InitializeAsync() => _initialization ??= LoadAsync();

    public void OnTick()
    {
        var now = _time.GetUtcNow();
        var delta = Math.Min((now - _lastTick).TotalSeconds, MaxTickSeconds);
        _lastTick = now;

        _engine.Tick(delta);
        RefreshAll();
        Toasts.CleanupExpired();

        if (++_ticksSinceSave >= TicksPerAutosave)
        {
            _ticksSinceSave = 0;
            _ = SaveAsync();
        }
    }

    public void OnSuspended()
    {
        _suspendedAt = _time.GetUtcNow();
        _logger.LogInformation("App suspended at {SuspendedAt:o}", _suspendedAt);
        _ = SaveAsync();
    }

    public void OnResumed()
    {
        var now = _time.GetUtcNow();
        _lastTick = now;

        if (_suspendedAt is not { } suspended) return;
        _suspendedAt = null;

        var elapsed = now - suspended;
        var earned = _engine.ApplyOfflineEarnings(elapsed);
        if (earned.Sign > 0)
        {
            _logger.LogInformation("Applied resume earnings: {Earned} for {Seconds:F0}s suspended", earned.ToCanonicalString(), elapsed.TotalSeconds);
            Toasts.Show($"While you were away, you earned ${NumberFormatter.Format(earned)}!");
        }

        RefreshAll();
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

    [RelayCommand]
    private void Export()
    {
        TransferText = _engine.ExportToString();
        IsExportMode = true;
        IsTransferOpen = true;
        _feedback.Play(FeedbackCue.Tap);
        _logger.LogInformation("Exported game state ({Length} chars)", TransferText.Length);
    }

    [RelayCommand]
    private async Task CopyExportAsync()
    {
        try
        {
            if (Clipboard is { } clipboard && await clipboard.TrySetTextAsync(TransferText))
            {
                _feedback.Play(FeedbackCue.Tap);
                Toasts.Show("Copied to clipboard!");
            }
            else
            {
                Toasts.Show("Clipboard not available on this platform. Select all and copy manually.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy to clipboard");
            Toasts.Show("Could not copy — select the text and copy manually.");
        }
    }

    [RelayCommand]
    private void StartImport()
    {
        TransferText = "";
        IsExportMode = false;
        IsTransferOpen = true;
        _feedback.Play(FeedbackCue.Tap);
    }

    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (string.IsNullOrWhiteSpace(TransferText))
        {
            Deny("Paste an export string first!");
            return;
        }

        if (!_engine.ImportFromString(TransferText))
        {
            Deny("Invalid import string. Check and try again.");
            return;
        }

        RebuildBusinesses();
        await SaveAsync();
        CloseTransfer();
        _feedback.Play(FeedbackCue.Purchase);
        Toasts.Show("Progress imported successfully!");
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
            Deny("Prestige resets all businesses and cash, but you gain Angel Investors " +
                 "that permanently boost all revenue by +2% each. " +
                 "You need to earn more to unlock prestige (earn enough for at least 1 angel).");
            return;
        }

        var (angels, success) = _engine.Prestige();
        if (!success) return;

        RebuildBusinesses();
        await SaveAsync();
        _feedback.Play(FeedbackCue.Prestige);
        Toasts.Show($"Prestige! Gained {NumberFormatter.Format(angels)} angels. All revenue boosted!");
    }

    [RelayCommand]
    private Task ToggleSoundAsync()
    {
        SoundEnabled = !SoundEnabled;
        if (SoundEnabled) _feedback.Play(FeedbackCue.Tap);
        return PersistSettingsAsync();
    }

    [RelayCommand]
    private Task ToggleHapticsAsync()
    {
        HapticsEnabled = !HapticsEnabled;
        if (HapticsEnabled) _feedback.Play(FeedbackCue.Tap);
        return PersistSettingsAsync();
    }

    private async Task LoadAsync()
    {
        var settings = await _settings.LoadAsync();
        SoundEnabled = settings.SoundEnabled;
        HapticsEnabled = settings.HapticsEnabled;

        await _engine.LoadAsync();
        _lastTick = _time.GetUtcNow();
        RebuildBusinesses();
        _logger.LogInformation("Game initialized with {Count} businesses", Businesses.Count);
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync(new PlayerSettings(SoundEnabled, HapticsEnabled));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    private void RebuildBusinesses()
    {
        Businesses.Clear();
        foreach (var business in _engine.Businesses)
        {
            Businesses.Add(new BusinessViewModel(business, _engine, Toasts, _feedback));
        }
        RefreshAll();
    }

    private void Deny(string message)
    {
        _feedback.Play(FeedbackCue.Denied);
        Toasts.Show(message);
    }

    private void RefreshAll()
    {
        CashText = $"${NumberFormatter.Format(_engine.Cash)}";
        AngelText = NumberFormatter.Format(_engine.AngelInvestors);
        AngelBonusText = $"×{NumberFormatter.Format(_engine.AngelBonus)}";
        PrestigeCount = _engine.PrestigeCount;

        var potentialAngels = _engine.PotentialAngels;
        CanPrestige = potentialAngels >= BigDouble.One;
        NextAngelText = potentialAngels.Sign > 0 ? NumberFormatter.Format(potentialAngels) : "0";
        PrestigeExplanation = CanPrestige
            ? $"Reset all businesses. Gain {NextAngelText} angels: income ×{NumberFormatter.Format(_engine.PrestigeIncomeMultiplier)}."
            : "Keep earning! Need enough lifetime earnings to gain at least 1 angel.";

        var angelBonus = _engine.AngelBonus;
        var crossBonus = _engine.CrossBusinessSpeedMultiplier;
        var cash = _engine.Cash;
        foreach (var business in Businesses)
        {
            business.Refresh(cash, angelBonus, crossBonus);
        }

        var minOwned = _engine.MinOwnedAcrossBusinesses;
        MinOwnedAcrossText = minOwned.ToString();
        HasCrossSpeedBonus = crossBonus > BigDouble.One;
        CrossSpeedText = HasCrossSpeedBonus ? $"×{NumberFormatter.Format(crossBonus)} Speed" : "×1 Speed";
        NextCrossThresholdText = CrossBusinessSpeedBonus.NextThreshold(minOwned) is { } nextThreshold
            ? $"{CrossBusinessSpeedBonus.UnitsToNext(minOwned)} more of every business → {nextThreshold}"
            : "All cross-business bonuses earned";
    }
}
