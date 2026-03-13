using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

/// <summary>
/// ViewModel wrapping a single Business for data binding.
/// Includes expanded detail properties for adaptive display.
/// </summary>
public partial class BusinessViewModel(
    Business model,
    GameEngine engine,
    ToastService toasts) : ViewModelBase
{
    public Business Model => model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Icon => model.Icon;
    public string Color => model.Color;

    // --- Core display ---
    [ObservableProperty] private int _owned;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasManager;
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _revenueText = "";
    [ObservableProperty] private string _managerCostText = "";
    [ObservableProperty] private bool _canAfford;
    [ObservableProperty] private bool _canAffordManager;

    // --- Extended detail properties ---
    [ObservableProperty] private string _cycleTimeText = "";
    [ObservableProperty] private string _revenuePerSecondText = "";
    [ObservableProperty] private int _affordableCount;
    [ObservableProperty] private string _affordableCountText = "";
    [ObservableProperty] private double _milestoneMultiplier = 1.0;
    [ObservableProperty] private string _milestoneMultiplierText = "×1";
    [ObservableProperty] private string _nextMilestoneText = "";
    [ObservableProperty] private int _unitsToNextMilestone;
    [ObservableProperty] private bool _hasNextMilestone;
    [ObservableProperty] private string _nextMilestoneRewardText = "";

    // --- Buy-to-milestone ---
    [ObservableProperty] private bool _canBuyToNextMilestone;
    [ObservableProperty] private string _buyToNextMilestoneText = "";

    [RelayCommand]
    private void ClickBusiness()
    {
        if (model.Owned <= 0)
        {
            if (!engine.BuyBusiness(model.Id))
            {
                var cost = NumberFormatter.Format(model.NextCost);
                toasts.Show($"Need ${cost} to buy your first {model.Name}");
            }
        }
        else
        {
            if (!engine.StartBusiness(model.Id) && model.IsRunning)
            {
                var remaining = model.CycleTimeSeconds * (1.0 - model.ProgressPercent / 100.0);
                toasts.Show($"{model.Name} is running — {remaining:F1}s left");
            }
        }
    }

    [RelayCommand]
    private void BuyBusiness()
    {
        if (!engine.BuyBusiness(model.Id))
        {
            var need = model.NextCost - engine.Cash;
            toasts.Show($"Need ${NumberFormatter.Format(need)} more for next {model.Name}");
        }
    }

    [RelayCommand]
    private void BuyManager()
    {
        if (model.HasManager)
        {
            toasts.Show($"{model.Name} already has a manager");
            return;
        }

        if (!engine.BuyManager(model.Id))
        {
            var mgrCost = model.BaseCost * 1000;
            var need = mgrCost - engine.Cash;
            toasts.Show($"Need ${NumberFormatter.Format(need)} more for {model.Name} manager");
        }
    }

    [RelayCommand]
    private void BuyToNextMilestone()
    {
        var next = Milestone.NextMilestone(model.Owned);
        if (next is null)
        {
            toasts.Show($"{model.Name} has reached all milestones!");
            return;
        }

        var needed = next.Threshold - model.Owned;
        if (needed <= 0) return;

        var bought = engine.BuyMultiple(model.Id, needed);
        if (bought == 0)
        {
            toasts.Show($"Can't afford any more {model.Name} right now");
        }
        else if (bought < needed)
        {
            toasts.Show($"Bought {bought} {model.Name} — need {needed - bought} more for milestone");
        }
        else
        {
            toasts.Show($"Milestone reached! {model.Name} now at {model.Owned} ({next.Label})");
        }
    }

    /// <summary>Refresh all bindable properties from the model.</summary>
    public void Refresh(double cash)
    {
        Owned = model.Owned;
        ProgressPercent = model.ProgressPercent;
        IsRunning = model.IsRunning;
        HasManager = model.HasManager;
        CostText = NumberFormatter.Format(model.NextCost);
        RevenueText = model.Owned > 0 ? NumberFormatter.Format(model.Revenue) : "—";
        ManagerCostText = NumberFormatter.Format(model.BaseCost * 1000);
        CanAfford = cash >= model.NextCost;
        CanAffordManager = !model.HasManager && cash >= model.BaseCost * 1000;

        // Extended details
        CycleTimeText = FormatTime(model.CycleTimeSeconds);
        RevenuePerSecondText = model.Owned > 0
            ? $"${NumberFormatter.Format(model.RevenuePerSecond)}/s"
            : "—";

        AffordableCount = model.AffordableCount(cash);
        AffordableCountText = AffordableCount > 0 ? $"Can buy: {AffordableCount}" : "Can't afford";

        MilestoneMultiplier = model.MilestoneMultiplier;
        MilestoneMultiplierText = $"×{MilestoneMultiplier:G4}";

        var next = Milestone.NextMilestone(model.Owned);
        HasNextMilestone = next is not null;
        if (next is not null)
        {
            UnitsToNextMilestone = next.Threshold - model.Owned;
            NextMilestoneText = $"{UnitsToNextMilestone} more → {next.Threshold}";
            NextMilestoneRewardText = next.Label;

            // Can we afford at least 1 unit toward the milestone?
            CanBuyToNextMilestone = cash >= model.NextCost && UnitsToNextMilestone > 0;
            BuyToNextMilestoneText = $"BUY {UnitsToNextMilestone}→{next.Threshold}";
        }
        else
        {
            UnitsToNextMilestone = 0;
            NextMilestoneText = "All milestones reached!";
            NextMilestoneRewardText = "";
            CanBuyToNextMilestone = false;
            BuyToNextMilestoneText = "";
        }
    }

    private static string FormatTime(double seconds) => seconds switch
    {
        < 1 => $"{seconds * 1000:F0}ms",
        < 60 => $"{seconds:F1}s",
        < 3600 => $"{seconds / 60:F1}m",
        _ => $"{seconds / 3600:F1}h"
    };
}
