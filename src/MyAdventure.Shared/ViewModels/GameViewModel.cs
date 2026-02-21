using System.Collections.ObjectModel;
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
    private DateTime _lastTick;
    private int _saveCounter;

    [ObservableProperty] private string _cashText = "$0.00";
    [ObservableProperty] private string _angelText = "0";
    [ObservableProperty] private string _angelBonusText = "+0%";
    [ObservableProperty] private int _prestigeCount;
    [ObservableProperty] private bool _canPrestige;
    [ObservableProperty] private string _nextAngelText = "0";
    [ObservableProperty] private string _prestigeExplanation = "";

    public ObservableCollection<BusinessViewModel> Businesses { get; } = [];
    public ToastService Toasts => _toasts;

    public GameViewModel(GameEngine engine, ILogger<GameViewModel> logger, ToastService toasts)
    {
        _engine = engine;
        _logger = logger;
        _toasts = toasts;
        _lastTick = DateTime.UtcNow;
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
        var now = DateTime.UtcNow;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

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
        AngelBonusText = $"+{(_engine.AngelBonus - 1) * 100:F0}%";
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

        foreach (var bvm in Businesses)
            bvm.Refresh(_engine.Cash);
    }
}
