using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Services;

namespace MyAdventure.Shared.ViewModels;

/// <summary>
/// Main game ViewModel. Drives the game loop and exposes all state for binding.
/// </summary>
public partial class GameViewModel : ViewModelBase
{
    private readonly GameEngine _engine;
    private readonly ILogger<GameViewModel> _logger;
    private DateTimeOffset _lastTick;
    private int _saveCounter;

    [ObservableProperty] private string _cashText = "$0.00";
    [ObservableProperty] private string _angelText = "0";
    [ObservableProperty] private string _angelBonusText = "+0%";
    [ObservableProperty] private int _prestigeCount;
    [ObservableProperty] private bool _canPrestige;
    [ObservableProperty] private string _nextAngelText = "0";

    public ObservableCollection<BusinessViewModel> Businesses { get; } = [];

    public GameViewModel(GameEngine engine, ILogger<GameViewModel> logger)
    {
        _engine = engine;
        _logger = logger;
        _lastTick = DateTimeOffset.UtcNow;
    }

    public async Task InitializeAsync()
    {
        await _engine.LoadAsync();

        Businesses.Clear();
        foreach (var biz in _engine.Businesses)
            Businesses.Add(new BusinessViewModel(biz, _engine));

        RefreshAll();
        _logger.LogInformation("Game initialized with {Count} businesses", Businesses.Count);
    }

    /// <summary>Called by the UI timer (~60fps).</summary>
    public void OnTick()
    {
        var now = DateTimeOffset.UtcNow;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Clamp delta to avoid huge jumps if app was suspended
        delta = Math.Min(delta, 1.0);

        _engine.Tick(delta);
        RefreshAll();

        // Auto-save every ~5 seconds (300 ticks at 60fps)
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
        var (angels, success) = _engine.Prestige();
        if (!success) return;

        _logger.LogInformation("Prestige! Gained {Angels:F0} angels", angels);

        // Rebuild business VMs
        Businesses.Clear();
        foreach (var biz in _engine.Businesses)
            Businesses.Add(new BusinessViewModel(biz, _engine));

        RefreshAll();
        await SaveAsync();
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

        foreach (var bvm in Businesses)
            bvm.Refresh(_engine.Cash);
    }
}
