using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;

namespace MyAdventure.Shared.ViewModels;

/// <summary>
/// ViewModel wrapping a single Business for data binding. Includes
/// expanded detail properties for adaptive display.
///
/// <para>
/// <b>BigDouble note:</b> the <see cref="Refresh"/> method takes
/// <see cref="BigDouble"/> for both cash and the angel bonus, mirroring
/// the engine's new types. Display text properties stay
/// <see cref="string"/>; formatting happens once here so the views can
/// bind directly without conversion overhead per frame.
/// </para>
///
/// <para>
/// <b>"Buy Max" support:</b> the second action button used to be the
/// "Buy N→milestone" button and disappeared once all milestones were
/// reached at 1000 owned — leaving the player with no bulk-purchase
/// option past that point. It is now always present:
/// </para>
/// <list type="bullet">
///   <item>While a next milestone exists, it reads
///         <c>"BUY N→threshold"</c> and buys exactly the units needed to
///         reach it.</item>
///   <item>Once all milestones are reached, it reads
///         <c>"BUY MAX (N)"</c> and buys as many units as the player can
///         currently afford.</item>
/// </list>
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

    // --- Bulk buy button ---
    /// <summary>
    /// True if at least one unit can be afforded toward the next bulk
    /// purchase (either next milestone, or "buy max" if no milestones remain).
    /// Wired to the bulk-buy button's <c>Opacity</c> via the BoolToOpacity
    /// converter so unaffordable buttons dim rather than vanishing.
    /// </summary>
    [ObservableProperty] private bool _canBulkBuy;

    /// <summary>
    /// Label shown on the bulk-buy button. "BUY N→threshold" while a
    /// milestone is reachable; "BUY MAX (N)" once all milestones are reached.
    /// </summary>
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
            var mgrCost = new BigDouble(model.BaseCost * 1000);
            var need = mgrCost - engine.Cash;
            toasts.Show($"Need ${NumberFormatter.Format(need)} more for {model.Name} manager");
        }
    }

    /// <summary>
    /// Bulk purchase action. While a milestone is still reachable, buys
    /// exactly the units needed to reach it (or as many as the player
    /// can afford if not all are affordable). Once all milestones are
    /// reached, buys as many units as the player can currently afford —
    /// the "buy max" behavior the player needs deep into the game.
    /// </summary>
    [RelayCommand]
    private void BulkBuy()
    {
        var next = Milestone.NextMilestone(model.Owned);
        if (next is null)
        {
            // No more milestones → "Buy Max" mode.
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

    /// <summary>
    /// Refresh all bindable properties from the model.
    /// </summary>
    /// <param name="cash">Current player cash, used for affordability flags.</param>
    /// <param name="angelBonus">
    /// The current angel multiplier from <see cref="GameEngine.AngelBonus"/>
    /// (e.g. 2.0 for +100%). Applied to <see cref="RevenueText"/> and
    /// <see cref="RevenuePerSecondText"/> so the UI shows what the player will
    /// actually earn — not the pre-bonus base values.
    /// </param>
    public void Refresh(BigDouble cash, BigDouble angelBonus)
    {
        Owned = model.Owned;
        ProgressPercent = model.ProgressPercent;
        IsRunning = model.IsRunning;
        HasManager = model.HasManager;
        CostText = NumberFormatter.Format(model.NextCost);

        // Owned == 0 still shows "—" because there's no business to earn from yet.
        RevenueText = model.Owned > 0
            ? NumberFormatter.Format(model.Revenue * angelBonus)
            : "—";
        var managerCost = new BigDouble(model.BaseCost * 1000);
        ManagerCostText = NumberFormatter.Format(managerCost);
        CanAfford = cash >= model.NextCost;
        CanAffordManager = !model.HasManager && cash >= managerCost;

        // Extended details
        CycleTimeText = FormatTime(model.CycleTimeSeconds);
        RevenuePerSecondText = model.Owned > 0
            ? $"${NumberFormatter.Format(model.RevenuePerSecond * angelBonus)}/s"
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

            // Milestone-mode label and affordability for the bulk-buy button.
            CanBulkBuy = cash >= model.NextCost && UnitsToNextMilestone > 0;
            BulkBuyText = $"BUY {UnitsToNextMilestone}→{next.Threshold}";
        }
        else
        {
            UnitsToNextMilestone = 0;
            NextMilestoneText = "All milestones reached!";
            NextMilestoneRewardText = "";

            // Buy-max mode: the button STAYS visible. Affordable count
            // drives both the label ("BUY MAX (N)") and the enable state.
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
