using MyAdventure.Core.Entities;
using MyAdventure.Core.Numerics;
using MyAdventure.Core.Services;
using MyAdventure.Shared.ViewModels;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class BusinessViewModelSpeedTests
{
    private static readonly BigDouble NoAngels = BigDouble.One;

    [Fact]
    public void Refresh_BelowFirstSpeedThreshold_HidesSpeedRow()
    {
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
        var biz = MakeBusiness(owned: 400);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1.0, 50), NoAngels);

        vm.SpeedMultiplier.ShouldBe(64.0);
        vm.HasSpeedBonus.ShouldBeTrue();
        vm.SpeedMultiplierText.ShouldBe("×64 Speed");
        vm.HasNextSpeedMilestone.ShouldBeFalse();
        vm.NextSpeedMilestoneText.ShouldBe("");
    }

    [Fact]
    public void Refresh_CycleTimeText_ReflectsSpeedMultiplier()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseTimeSeconds = 0.6 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.CycleTimeText.ShouldBe("75ms");
    }

    [Fact]
    public void Refresh_RevenuePerSecondText_CompoundsRevenueAndSpeed()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels);

        vm.RevenuePerSecondText.ShouldBe("$6.40 K/s");
    }

    [Fact]
    public void Refresh_WithCrossBusinessMultiplier_ScalesRevenueDisplay()
    {
        var biz = MakeBusiness(owned: 100);
        biz = biz with { BaseRevenue = 1.0, BaseTimeSeconds = 1.0, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), NoAngels, new BigDouble(2.0));

        vm.RevenuePerSecondText.ShouldBe("$12.80 K/s");

        vm.CycleTimeText.ShouldBe("125ms");
    }

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
        var engine = Ui.CreateEngine();
        return (Ui.CreateBusiness(biz, engine), engine);
    }
}
