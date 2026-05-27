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
/// <b>BigDouble note:</b> the <see cref="Refresh(BigDouble, BigDouble)"/>
/// method takes <see cref="BigDouble"/> for both cash and the angel
/// bonus, mirroring the engine's new types. Display text properties
/// stay <see cref="string"/>; formatting happens once here so the views
/// can bind directly without conversion overhead per frame.
/// </para>
///
/// <para>
/// <b>Cross-business bonus note:</b> the
/// <see cref="Refresh(BigDouble, BigDouble, BigDouble)"/> overload
/// takes a third parameter — the cross-business earnings multiplier
/// from <see cref="GameEngine.CrossBusinessSpeedMultiplier"/> — and
/// applies it to the displayed per-cycle revenue and revenue per
/// second so the UI shows what the player will actually earn. The
/// two-argument overload forwards with <see cref="BigDouble.One"/>
/// so existing call sites and tests are unaffected.
/// </para>
///
/// <para>
/// <b>"Buy Max" support:</b> the second action button used to be the
/// "Buy N→milestone" button and disappeared once all milestones were
/// reached at 1000 owned — leaving the player with no bulk-purchase
/// option past that point. It is now always present:
/// </para>
/// <list type="bullet">
///   <item>While a next revenue milestone exists, it reads
///         <c>"BUY N→threshold"</c> and buys exactly the units needed to
///         reach it.</item>
///   <item>Once all revenue milestones are reached, it reads
///         <c>"BUY MAX (N)"</c> and buys as many units as the player can
///         currently afford.</item>
/// </list>
///
/// <para>
/// <b>Speed milestone display:</b> separate properties expose the
/// per-business speed multiplier (e.g. "×64 Speed") and the next speed
/// milestone so the UI can show how cycle time is improving alongside
/// revenue. This is orthogonal to the revenue milestone display — both
/// are visible in the detail panel. The cross-business bonus is shown
/// at the global level (<see cref="GameViewModel"/>) since it applies
/// uniformly to every business.
/// </para>
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
    /// <summary>
    /// Mirror of <see cref="Business.Owned"/>. Widened to <see cref="long"/>
    /// alongside the model so the View can bind to player ownership counts
    /// that exceed 2³¹ — see the type-level remark on <see cref="Business"/>
    /// for the overflow story that drove the widening.
    /// </summary>
    [ObservableProperty] private long _owned;
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
    /// <summary>
    /// Mirror of <see cref="Business.AffordableCount(MyAdventure.Core.Numerics.BigDouble)"/>.
    /// Widened to <see cref="long"/> alongside the engine so the "buy max"
    /// label can display the analytic affordable count even past 2³¹.
    /// </summary>
    [ObservableProperty] private long _affordableCount;
    [ObservableProperty] private string _affordableCountText = "";
    [ObservableProperty] private double _milestoneMultiplier = 1.0;
    [ObservableProperty] private string _milestoneMultiplierText = "×1";
    [ObservableProperty] private string _nextMilestoneText = "";
    /// <summary>
    /// Units remaining to reach the next revenue milestone. Widened to
    /// <see cref="long"/> because <see cref="Business.Owned"/> is now
    /// <see cref="long"/>; while the milestone thresholds themselves
    /// stay <see cref="int"/>, the gap <c>Threshold − Owned</c> is
    /// computed in long-space to match.
    /// </summary>
    [ObservableProperty] private long _unitsToNextMilestone;
    [ObservableProperty] private bool _hasNextMilestone;
    [ObservableProperty] private string _nextMilestoneRewardText = "";

    // --- Speed milestone properties (per-business) ---
    /// <summary>Compounded per-business speed multiplier (e.g. 64.0 for ×64 Speed). 1.0 below the first speed threshold.</summary>
    [ObservableProperty] private double _speedMultiplier = 1.0;

    /// <summary>Human-readable text for the speed multiplier (e.g. "×64 Speed"). Hidden when 1.0 via <see cref="HasSpeedBonus"/>.</summary>
    [ObservableProperty] private string _speedMultiplierText = "×1 Speed";

    /// <summary>True when speed multiplier exceeds 1.0 — drives visibility so the row doesn't clutter early-game cards.</summary>
    [ObservableProperty] private bool _hasSpeedBonus;

    /// <summary>True when there is a next speed milestone the player has not yet reached.</summary>
    [ObservableProperty] private bool _hasNextSpeedMilestone;

    /// <summary>Text describing the next speed milestone progress (e.g. "20 more → 100 (×2 Speed)"). Empty when none remain.</summary>
    [ObservableProperty] private string _nextSpeedMilestoneText = "";

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
    /// Refresh all bindable properties from the model, applying ONLY the
    /// angel bonus to revenue displays. Equivalent to calling the
    /// three-argument overload with <see cref="BigDouble.One"/> as the
    /// cross-business multiplier — preserved for backward compatibility
    /// with the existing test suite (which predates the cross-business
    /// bonus feature) and any external callers.
    /// </summary>
    public void Refresh(BigDouble cash, BigDouble angelBonus) =>
        Refresh(cash, angelBonus, BigDouble.One);

    /// <summary>
    /// Refresh all bindable properties from the model, applying both the
    /// angel bonus and the cross-business multiplier to revenue displays.
    /// </summary>
    /// <param name="cash">Current player cash, used for affordability flags.</param>
    /// <param name="angelBonus">
    /// The current angel multiplier from <see cref="GameEngine.AngelBonus"/>
    /// (e.g. 2.0 for +100%). Applied to <see cref="RevenueText"/> and
    /// <see cref="RevenuePerSecondText"/> so the UI shows what the player
    /// will actually earn — not the pre-bonus base values.
    /// </param>
    /// <param name="crossBusinessSpeed">
    /// The current cross-business earnings multiplier from
    /// <see cref="GameEngine.CrossBusinessSpeedMultiplier"/>. Applied to
    /// the same displayed revenue values so the UI matches the engine's
    /// actual per-tick payout. Pass <see cref="BigDouble.One"/> when not
    /// applicable.
    /// </param>
    public void Refresh(BigDouble cash, BigDouble angelBonus, BigDouble crossBusinessSpeed)
    {
        Owned = model.Owned;
        ProgressPercent = model.ProgressPercent;
        IsRunning = model.IsRunning;
        HasManager = model.HasManager;
        CostText = NumberFormatter.Format(model.NextCost);

        // Pre-compute the combined multiplier once per Refresh. Both
        // values are BigDouble, so the product stays representable at
        // any scale.
        var totalEarningsBonus = angelBonus * crossBusinessSpeed;

        // Owned == 0 still shows "—" because there's no business to earn from yet.
        RevenueText = model.Owned > 0
            ? NumberFormatter.Format(model.Revenue * totalEarningsBonus)
            : "—";
        var managerCost = new BigDouble(model.BaseCost * 1000);
        ManagerCostText = NumberFormatter.Format(managerCost);
        CanAfford = cash >= model.NextCost;
        CanAffordManager = !model.HasManager && cash >= managerCost;

        // Extended details — cycle time reflects the per-business speed
        // milestones via Business.CycleTimeSeconds. The cross-business
        // bonus is NOT folded into cycle time (it's a revenue multiplier),
        // so the displayed cycle time honestly reports how often a single
        // cycle of this business fires.
        CycleTimeText = FormatTime(model.CycleTimeSeconds);
        RevenuePerSecondText = model.Owned > 0
            ? $"${NumberFormatter.Format(model.RevenuePerSecond * totalEarningsBonus)}/s"
            : "—";

        AffordableCount = model.AffordableCount(cash);
        AffordableCountText = AffordableCount > 0 ? $"Can buy: {AffordableCount}" : "Can't afford";

        MilestoneMultiplier = model.MilestoneMultiplier;
        MilestoneMultiplierText = $"×{MilestoneMultiplier:G4}";

        // Speed multiplier display (per-business only). Hidden when 1.0
        // to keep early-game cards uncluttered (HasSpeedBonus is the
        // visibility flag). The cross-business bonus is displayed
        // globally on GameViewModel rather than per-card.
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
