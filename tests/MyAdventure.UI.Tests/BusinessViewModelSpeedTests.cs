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
/// once speed scaling is active.
/// </para>
/// </summary>
public class BusinessViewModelSpeedTests
{
    private static readonly BigDouble NoAngels = BigDouble.One;

    [Fact]
    public void Refresh_BelowFirstSpeedThreshold_HidesSpeedRow()
    {
        var biz = MakeBusiness(owned: 50);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.SpeedMultiplier.ShouldBe(1.0);
        vm.HasSpeedBonus.ShouldBeFalse();
        vm.HasNextSpeedMilestone.ShouldBeTrue();
        vm.NextSpeedMilestoneText.ShouldContain("100");
        vm.NextSpeedMilestoneText.ShouldContain("Speed");
    }

    [Fact]
    public void Refresh_At100_ShowsTwoSpeed()
    {
        var biz = MakeBusiness(owned: 100);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.SpeedMultiplier.ShouldBe(2.0);
        vm.HasSpeedBonus.ShouldBeTrue();
        vm.SpeedMultiplierText.ShouldBe("×2 Speed");
        vm.HasNextSpeedMilestone.ShouldBeTrue();
        vm.NextSpeedMilestoneText.ShouldContain("200");
    }

    [Fact]
    public void Refresh_At400_ShowsMaxSpeedAndNoNext()
    {
        var biz = MakeBusiness(owned: 400);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1.0, 50), NoAngels);

        vm.SpeedMultiplier.ShouldBe(16.0);
        vm.HasSpeedBonus.ShouldBeTrue();
        vm.SpeedMultiplierText.ShouldBe("×16 Speed");
        vm.HasNextSpeedMilestone.ShouldBeFalse();
        vm.NextSpeedMilestoneText.ShouldBe("");
    }

    /// <summary>
    /// The displayed cycle time must reflect the speed milestone, not
    /// the base time — the player should see "300ms" once they hit 100
    /// owned of a 0.6 s base business.
    /// </summary>
    [Fact]
    public void Refresh_CycleTimeText_ReflectsSpeedMultiplier()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseTimeSeconds = 0.6 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        // 0.6s × 0.5 = 0.3s → "300ms" via the sub-1s formatter branch.
        vm.CycleTimeText.ShouldBe("300ms");
    }

    /// <summary>
    /// Revenue per second must compound revenue and speed milestones —
    /// this is the core promise of the speed system. At 100 owned with
    /// $1 base revenue and 1.0 s base cycle, EPS = 100 × ×8 revenue
    /// milestone × ×2 speed = $1600/s.
    /// </summary>
    [Fact]
    public void Refresh_RevenuePerSecondText_CompoundsRevenueAndSpeed()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.RevenuePerSecondText.ShouldBe("$1.60 K/s");
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
