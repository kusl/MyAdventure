using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Feedback;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

public sealed partial class BusinessViewModel(
    Business model,
    GameEngine engine,
    ToastService toasts,
    IFeedbackService feedback) : ObservableObject
{
    public Business Model => model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Icon => model.Icon;
    public string Color => model.Color;

    public long Owned { get; private set => SetProperty(ref field, value); }
    public bool IsOwned { get; private set => SetProperty(ref field, value); }
    public double ProgressPercent { get; private set => SetProperty(ref field, value); }
    public bool IsRunning { get; private set => SetProperty(ref field, value); }
    public bool HasManager { get; private set => SetProperty(ref field, value); }
    public string CostText { get; private set => SetProperty(ref field, value); } = "";
    public string RevenueText { get; private set => SetProperty(ref field, value); } = "";
    public string ManagerCostText { get; private set => SetProperty(ref field, value); } = "";
    public bool CanAfford { get; private set => SetProperty(ref field, value); }
    public bool CanAffordManager { get; private set => SetProperty(ref field, value); }
    public string CycleTimeText { get; private set => SetProperty(ref field, value); } = "";
    public string RevenuePerSecondText { get; private set => SetProperty(ref field, value); } = "";
    public long AffordableCount { get; private set => SetProperty(ref field, value); }
    public string AffordableCountText { get; private set => SetProperty(ref field, value); } = "";
    public double MilestoneMultiplier { get; private set => SetProperty(ref field, value); } = 1.0;
    public string MilestoneMultiplierText { get; private set => SetProperty(ref field, value); } = "×1";
    public string NextMilestoneText { get; private set => SetProperty(ref field, value); } = "";
    public long UnitsToNextMilestone { get; private set => SetProperty(ref field, value); }
    public bool HasNextMilestone { get; private set => SetProperty(ref field, value); }
    public string NextMilestoneRewardText { get; private set => SetProperty(ref field, value); } = "";
    public double SpeedMultiplier { get; private set => SetProperty(ref field, value); } = 1.0;
    public string SpeedMultiplierText { get; private set => SetProperty(ref field, value); } = "×1 Speed";
    public bool HasSpeedBonus { get; private set => SetProperty(ref field, value); }
    public bool HasNextSpeedMilestone { get; private set => SetProperty(ref field, value); }
    public string NextSpeedMilestoneText { get; private set => SetProperty(ref field, value); } = "";
    public bool CanBulkBuy { get; private set => SetProperty(ref field, value); }
    public string BulkBuyText { get; private set => SetProperty(ref field, value); } = "";

    [RelayCommand]
    private void ClickBusiness()
    {
        if (model.Owned <= 0)
        {
            if (TryPurchase(() => engine.BuyBusiness(model.Id))) return;
            Deny($"Need ${NumberFormatter.Format(model.NextCost)} to buy your first {model.Name}");
            return;
        }

        if (engine.StartBusiness(model.Id))
        {
            feedback.Play(FeedbackCue.Tap);
            return;
        }

        if (model.IsRunning)
        {
            var remaining = model.CycleTimeSeconds * (1.0 - model.ProgressPercent / 100.0);
            toasts.Show($"{model.Name} is running — {remaining:F1}s left");
        }
    }

    [RelayCommand]
    private void BuyBusiness()
    {
        if (TryPurchase(() => engine.BuyBusiness(model.Id))) return;
        Deny($"Need ${NumberFormatter.Format(model.NextCost - engine.Cash)} more for next {model.Name}");
    }

    [RelayCommand]
    private void BuyManager()
    {
        if (model.HasManager)
        {
            toasts.Show($"{model.Name} already has a manager");
            return;
        }

        if (engine.BuyManager(model.Id))
        {
            feedback.Play(FeedbackCue.Manager);
            return;
        }

        Deny($"Need ${NumberFormatter.Format(GameBalance.ManagerCost(model) - engine.Cash)} more for {model.Name} manager");
    }

    [RelayCommand]
    private void BulkBuy()
    {
        if (Milestone.NextMilestone(model.Owned) is not { } next)
        {
            if (model.AffordableCount(engine.Cash) <= 0)
            {
                Deny($"Can't afford any more {model.Name} right now");
                return;
            }

            var bought = 0L;
            if (TryPurchase(() => (bought = engine.BuyMax(model.Id)) > 0))
            {
                toasts.Show($"Bought {bought} more {model.Name} (now {model.Owned})");
            }
            return;
        }

        var needed = next.Threshold - model.Owned;
        if (needed <= 0) return;

        var purchased = 0L;
        if (!TryPurchase(() => (purchased = engine.BuyMultiple(model.Id, needed)) > 0))
        {
            Deny($"Can't afford any more {model.Name} right now");
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

    public void Refresh(BigDouble cash, BigDouble angelBonus) => Refresh(cash, angelBonus, BigDouble.One);

    public void Refresh(BigDouble cash, BigDouble angelBonus, BigDouble crossBusinessSpeed)
    {
        Owned = model.Owned;
        IsOwned = model.Owned > 0;
        ProgressPercent = model.ProgressPercent;
        IsRunning = model.IsRunning;
        HasManager = model.HasManager;
        CostText = NumberFormatter.Format(model.NextCost);

        var earningsBonus = angelBonus * crossBusinessSpeed;
        RevenueText = IsOwned ? NumberFormatter.Format(model.Revenue * earningsBonus) : "—";

        var managerCost = GameBalance.ManagerCost(model);
        ManagerCostText = NumberFormatter.Format(managerCost);
        CanAfford = cash >= model.NextCost;
        CanAffordManager = !model.HasManager && cash >= managerCost;

        CycleTimeText = FormatTime(model.CycleTimeSeconds);
        RevenuePerSecondText = IsOwned ? $"${NumberFormatter.Format(model.RevenuePerSecond * earningsBonus)}/s" : "—";

        AffordableCount = model.AffordableCount(cash);
        AffordableCountText = AffordableCount > 0 ? $"Can buy: {AffordableCount}" : "Can't afford";

        MilestoneMultiplier = model.MilestoneMultiplier;
        MilestoneMultiplierText = $"×{MilestoneMultiplier:G4}";

        SpeedMultiplier = model.SpeedMultiplier;
        SpeedMultiplierText = $"×{SpeedMultiplier:G4} Speed";
        HasSpeedBonus = SpeedMultiplier > 1.0;

        var nextSpeed = SpeedMilestone.NextSpeedMilestone(model.Owned);
        HasNextSpeedMilestone = nextSpeed is not null;
        NextSpeedMilestoneText = nextSpeed is null ? "" : $"{nextSpeed.Threshold - model.Owned} more → {nextSpeed.Threshold} ({nextSpeed.Label})";

        if (Milestone.NextMilestone(model.Owned) is { } next)
        {
            HasNextMilestone = true;
            UnitsToNextMilestone = next.Threshold - model.Owned;
            NextMilestoneText = $"{UnitsToNextMilestone} more → {next.Threshold}";
            NextMilestoneRewardText = next.Label;
            CanBulkBuy = CanAfford && UnitsToNextMilestone > 0;
            BulkBuyText = $"BUY {UnitsToNextMilestone}→{next.Threshold}";
        }
        else
        {
            HasNextMilestone = false;
            UnitsToNextMilestone = 0;
            NextMilestoneText = "All milestones reached!";
            NextMilestoneRewardText = "";
            CanBulkBuy = AffordableCount > 0;
            BulkBuyText = AffordableCount > 0 ? $"BUY MAX ({AffordableCount})" : "BUY MAX";
        }
    }

    private bool TryPurchase(Func<bool> purchase)
    {
        var milestoneBefore = model.MilestoneMultiplier;
        var speedBefore = model.SpeedMultiplier;
        if (!purchase()) return false;

        var crossedMilestone = model.MilestoneMultiplier > milestoneBefore || model.SpeedMultiplier > speedBefore;
        feedback.Play(crossedMilestone ? FeedbackCue.Milestone : FeedbackCue.Purchase);
        return true;
    }

    private void Deny(string message)
    {
        feedback.Play(FeedbackCue.Denied);
        toasts.Show(message);
    }

    private static string FormatTime(double seconds) => seconds switch
    {
        < 1 => $"{seconds * 1000:F0}ms",
        < 60 => $"{seconds:F1}s",
        < 3600 => $"{seconds / 60:F1}m",
        _ => $"{seconds / 3600:F1}h",
    };
}
