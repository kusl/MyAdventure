using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

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

    [ObservableProperty] private long _owned;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasManager;
    [ObservableProperty] private string _costText = "";
    [ObservableProperty] private string _revenueText = "";
    [ObservableProperty] private string _managerCostText = "";
    [ObservableProperty] private bool _canAfford;
    [ObservableProperty] private bool _canAffordManager;

    [ObservableProperty] private string _cycleTimeText = "";
    [ObservableProperty] private string _revenuePerSecondText = "";
    [ObservableProperty] private long _affordableCount;
    [ObservableProperty] private string _affordableCountText = "";
    [ObservableProperty] private double _milestoneMultiplier = 1.0;
    [ObservableProperty] private string _milestoneMultiplierText = "×1";
    [ObservableProperty] private string _nextMilestoneText = "";
    [ObservableProperty] private long _unitsToNextMilestone;
    [ObservableProperty] private bool _hasNextMilestone;
    [ObservableProperty] private string _nextMilestoneRewardText = "";

    [ObservableProperty] private double _speedMultiplier = 1.0;

    [ObservableProperty] private string _speedMultiplierText = "×1 Speed";

    [ObservableProperty] private bool _hasSpeedBonus;

    [ObservableProperty] private bool _hasNextSpeedMilestone;

    [ObservableProperty] private string _nextSpeedMilestoneText = "";

    [ObservableProperty] private bool _canBulkBuy;

    [ObservableProperty] private string _bulkBuyText = "";

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
            var mgrCost = GameBalance.ManagerCost(model);
            var need = mgrCost - engine.Cash;
            toasts.Show($"Need ${NumberFormatter.Format(need)} more for {model.Name} manager");
        }
    }

    [RelayCommand]
    private void BulkBuy()
    {
        var next = Milestone.NextMilestone(model.Owned);
        if (next is null)
        {
            var affordable = model.AffordableCount(engine.Cash);
            if (affordable <= 0)
            {
                toasts.Show($"Can't afford any more {model.Name} right now");
                return;
            }
            var bought = engine.BuyMax(model.Id);
            if (bought > 0)
            {
                toasts.Show($"Bought {bought} more {model.Name} (now {model.Owned})");
            }
            return;
        }

        var needed = next.Threshold - model.Owned;
        if (needed <= 0) return;

        var purchased = engine.BuyMultiple(model.Id, needed);
        if (purchased == 0)
        {
            toasts.Show($"Can't afford any more {model.Name} right now");
        }
        else if (purchased < needed)
        {
            toasts.Show($"Bought {purchased} {model.Name} — need {needed - purchased} more for milestone");
        }
        else
        {
            toasts.Show($"Milestone reached! {model.Name} now at {model.Owned} ({next.Label})");
        }
    }

    public void Refresh(BigDouble cash, BigDouble angelBonus) =>
        Refresh(cash, angelBonus, BigDouble.One);

    public void Refresh(BigDouble cash, BigDouble angelBonus, BigDouble crossBusinessSpeed)
    {
        Owned = model.Owned;
        ProgressPercent = model.ProgressPercent;
        IsRunning = model.IsRunning;
        HasManager = model.HasManager;
        CostText = NumberFormatter.Format(model.NextCost);

        var totalEarningsBonus = angelBonus * crossBusinessSpeed;

        RevenueText = model.Owned > 0
            ? NumberFormatter.Format(model.Revenue * totalEarningsBonus)
            : "—";
        var managerCost = GameBalance.ManagerCost(model);
        ManagerCostText = NumberFormatter.Format(managerCost);
        CanAfford = cash >= model.NextCost;
        CanAffordManager = !model.HasManager && cash >= managerCost;

        CycleTimeText = FormatTime(model.CycleTimeSeconds);
        RevenuePerSecondText = model.Owned > 0
            ? $"${NumberFormatter.Format(model.RevenuePerSecond * totalEarningsBonus)}/s"
            : "—";

        AffordableCount = model.AffordableCount(cash);
        AffordableCountText = AffordableCount > 0 ? $"Can buy: {AffordableCount}" : "Can't afford";

        MilestoneMultiplier = model.MilestoneMultiplier;
        MilestoneMultiplierText = $"×{MilestoneMultiplier:G4}";

        SpeedMultiplier = model.SpeedMultiplier;
        SpeedMultiplierText = $"×{SpeedMultiplier:G4} Speed";
        HasSpeedBonus = SpeedMultiplier > 1.0;

        var nextSpeed = SpeedMilestone.NextSpeedMilestone(model.Owned);
        HasNextSpeedMilestone = nextSpeed is not null;
        NextSpeedMilestoneText = nextSpeed is not null
            ? $"{nextSpeed.Threshold - model.Owned} more → {nextSpeed.Threshold} ({nextSpeed.Label})"
            : "";

        var next = Milestone.NextMilestone(model.Owned);
        HasNextMilestone = next is not null;
        if (next is not null)
        {
            UnitsToNextMilestone = next.Threshold - model.Owned;
            NextMilestoneText = $"{UnitsToNextMilestone} more → {next.Threshold}";
            NextMilestoneRewardText = next.Label;

            CanBulkBuy = cash >= model.NextCost && UnitsToNextMilestone > 0;
            BulkBuyText = $"BUY {UnitsToNextMilestone}→{next.Threshold}";
        }
        else
        {
            UnitsToNextMilestone = 0;
            NextMilestoneText = "All milestones reached!";
            NextMilestoneRewardText = "";

            CanBulkBuy = AffordableCount > 0;
            BulkBuyText = AffordableCount > 0 ? $"BUY MAX ({AffordableCount})" : "BUY MAX";
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
