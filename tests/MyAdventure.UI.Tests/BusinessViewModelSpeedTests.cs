using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

/// <summary>
/// Speed-milestone-related properties on <see cref="BusinessViewModel"/>:
/// <see cref="BusinessViewModel.SpeedMultiplier"/>,
/// <see cref="BusinessViewModel.SpeedMultiplierText"/>,
/// <see cref="BusinessViewModel.HasSpeedBonus"/>,
/// <see cref="BusinessViewModel.HasNextSpeedMilestone"/>, and
/// <see cref="BusinessViewModel.NextSpeedMilestoneText"/>.
///
/// <para>
/// These properties drive an adaptive UI: the speed row is hidden
/// while the player is below the first speed threshold (so early-game
/// cards stay simple), and only the relevant progress message is shown
/// once speed scaling is active. The first speed threshold is now 25
/// (AdCap-parity update from the previous 100); tests below have been
/// adjusted for the new ladder of 25/50/100/200/300/400 → ×64 max.
/// </para>
/// </summary>
public class BusinessViewModelSpeedTests
{
    private static readonly BigDouble NoAngels = BigDouble.One;

    [Fact]
    public void Refresh_BelowFirstSpeedThreshold_HidesSpeedRow()
    {
        // Owned 24 is now strictly below the first speed threshold (25);
        // the old test used 50, which under the new six-threshold ladder
        // is itself a speed milestone, so the bound moved down.
        var biz = MakeBusiness(owned: 24);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.SpeedMultiplier.ShouldBe(1.0);
        vm.HasSpeedBonus.ShouldBeFalse();
        vm.HasNextSpeedMilestone.ShouldBeTrue();
        vm.NextSpeedMilestoneText.ShouldContain("25");
        vm.NextSpeedMilestoneText.ShouldContain("Speed");
    }

    [Fact]
    public void Refresh_At25_ShowsTwoSpeed()
    {
        var biz = MakeBusiness(owned: 25);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.SpeedMultiplier.ShouldBe(2.0);
        vm.HasSpeedBonus.ShouldBeTrue();
        vm.SpeedMultiplierText.ShouldBe("×2 Speed");
        vm.HasNextSpeedMilestone.ShouldBeTrue();
        vm.NextSpeedMilestoneText.ShouldContain("50");
    }

    [Fact]
    public void Refresh_At100_ShowsEightSpeed()
    {
        // At 100 owned, three speed milestones have fired (25, 50, 100)
        // — so the compounded multiplier is 2^3 = 8. Under the old
        // four-threshold table this was 2× (only 100 had fired); the new
        // ladder produces a 4× larger multiplier at this same ownership.
        var biz = MakeBusiness(owned: 100);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.SpeedMultiplier.ShouldBe(8.0);
        vm.HasSpeedBonus.ShouldBeTrue();
        vm.SpeedMultiplierText.ShouldBe("×8 Speed");
        vm.HasNextSpeedMilestone.ShouldBeTrue();
        vm.NextSpeedMilestoneText.ShouldContain("200");
    }

    [Fact]
    public void Refresh_At400_ShowsMaxSpeedAndNoNext()
    {
        // All six speed milestones fire by 400 owned: 2^6 = 64.
        var biz = MakeBusiness(owned: 400);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1.0, 50), NoAngels);

        vm.SpeedMultiplier.ShouldBe(64.0);
        vm.HasSpeedBonus.ShouldBeTrue();
        vm.SpeedMultiplierText.ShouldBe("×64 Speed");
        vm.HasNextSpeedMilestone.ShouldBeFalse();
        vm.NextSpeedMilestoneText.ShouldBe("");
    }

    /// <summary>
    /// The displayed cycle time must reflect the speed milestone, not
    /// the base time — at 100 owned of a 0.6 s base business with the
    /// new ladder, three milestones fire (×8 speed), so the effective
    /// cycle is 0.6 / 8 = 0.075 s → "75ms".
    /// </summary>
    [Fact]
    public void Refresh_CycleTimeText_ReflectsSpeedMultiplier()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseTimeSeconds = 0.6 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        // 0.6s × 0.125 = 0.075s → "75ms" via the sub-1s formatter branch.
        vm.CycleTimeText.ShouldBe("75ms");
    }

    /// <summary>
    /// Revenue per second must compound revenue and speed milestones —
    /// this is the core promise of the speed system. At 100 owned with
    /// $1 base revenue and 1.0 s base cycle, EPS = (100 × $1 × ×8 revenue
    /// milestone) ÷ (1.0 × 0.125 speed multiplier) = $800 / 0.125s
    /// = $6400/s, formatted as "$6.40 K/s" by NumberFormatter's
    /// thousands-rule.
    ///
    /// <para>
    /// This is 4× higher than the prior expected value of "$1.60 K/s"
    /// because the speed ladder has been buffed: the old test ran
    /// against the four-threshold table (×2 at 100); the new six-
    /// threshold table fires three speed milestones by 100 (×8).
    /// </para>
    /// </summary>
    [Fact]
    public void Refresh_RevenuePerSecondText_CompoundsRevenueAndSpeed()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.RevenuePerSecondText.ShouldBe("$6.40 K/s");
    }

    /// <summary>
    /// New: when a cross-business multiplier is passed to the 3-arg
    /// Refresh overload, both the per-cycle revenue and the per-second
    /// rate displayed must scale by it. The per-business speed cycle
    /// time is NOT multiplied — cross-business is a revenue multiplier,
    /// not a cycle-time divisor (see <see cref="CrossBusinessSpeedBonus"/>
    /// XML docs for the numerical-stability rationale).
    /// </summary>
    [Fact]
    public void Refresh_WithCrossBusinessMultiplier_ScalesRevenueDisplay()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        // Pass a ×2 cross-business multiplier. Compared to the
        // no-cross-bonus case ($6.40 K/s), revenue per second should now
        // display $12.80 K/s.
        vm.Refresh(new BigDouble(1_000_000), NoAngels, new BigDouble(2.0));

        vm.RevenuePerSecondText.ShouldBe("$12.80 K/s");

        // Cycle time displayed is UNCHANGED by the cross-business
        // multiplier — the per-business cycle is still 0.125s (1.0 × 0.125
        // from speed milestones alone).
        vm.CycleTimeText.ShouldBe("125ms");
    }

    /// <summary>
    /// The two-argument <c>Refresh(cash, angelBonus)</c> overload must
    /// behave exactly as if called with <c>crossBusinessSpeed = BigDouble.One</c>.
    /// This pins backward compatibility — existing test sites and any
    /// external callers that haven't been migrated to the 3-arg form
    /// must continue producing identical output.
    /// </summary>
    [Fact]
    public void Refresh_TwoArgOverload_EquivalentToThreeArgWithOne()
    {
        var biz1 = MakeBusiness(owned: 100);
        biz1 = biz1 with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm1, _) = MakeVm(biz1);

        var biz2 = MakeBusiness(owned: 100);
        biz2 = biz2 with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm2, _) = MakeVm(biz2);

        vm1.Refresh(new BigDouble(1_000_000), NoAngels);
        vm2.Refresh(new BigDouble(1_000_000), NoAngels, BigDouble.One);

        vm2.RevenuePerSecondText.ShouldBe(vm1.RevenuePerSecondText);
        vm2.RevenueText.ShouldBe(vm1.RevenueText);
        vm2.CycleTimeText.ShouldBe(vm1.CycleTimeText);
        vm2.SpeedMultiplier.ShouldBe(vm1.SpeedMultiplier);
    }

    private static Business MakeBusiness(int owned = 0) => new()
    {
        Id = "test",
        Name = "Test",
        Icon = "T",
        Color = "#FFF",
        BaseCost = 1,
        BaseRevenue = 10,
        BaseTimeSeconds = 1,
        CostMultiplier = 1.01,
        Owned = owned
    };

    private static (BusinessViewModel vm, GameEngine engine) MakeVm(Business biz)
    {
        var repo = Substitute.For<IGameStateRepository>();
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        var vm = new BusinessViewModel(biz, engine, toasts);
        return (vm, engine);
    }
}
