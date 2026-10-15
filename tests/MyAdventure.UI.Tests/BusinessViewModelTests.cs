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

public class BusinessViewModelTests
{
    private static readonly BigDouble NoAngels = BigDouble.One;

    [Fact]
    public void Refresh_ShouldUpdateAllProperties()
    {
        var biz = MakeBusiness(owned: 3);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(500), NoAngels);

        vm.Owned.ShouldBe(3);
        vm.CostText.ShouldNotBeNullOrEmpty();
        vm.RevenueText.ShouldNotBe("—");
        vm.CanAfford.ShouldBeTrue();
    }

    [Fact]
    public void Refresh_NotEnoughCash_ShouldShowNotAffordable()
    {
        var biz = MakeBusiness();
        biz = biz with { BaseCost = 1000 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(5), NoAngels);
        vm.CanAfford.ShouldBeFalse();
    }

    [Fact]
    public void Refresh_BeforeMilestone_ShowsBuyToMilestoneLabel()
    {
        var biz = MakeBusiness(owned: 20);
        biz = biz with { BaseCost = 10, CostMultiplier = 1.05 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(10_000), NoAngels);

        vm.HasNextMilestone.ShouldBeTrue();
        vm.BulkBuyText.ShouldBe("BUY 5→25");
        vm.CanBulkBuy.ShouldBeTrue();
    }

    [Fact]
    public void Refresh_AllMilestonesReached_BulkBuyButtonStaysVisibleAsBuyMax()
    {
        var biz = MakeBusiness(owned: 1000);
        biz = biz with { BaseCost = 10, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1.0, 50), NoAngels);

        vm.HasNextMilestone.ShouldBeFalse();
        vm.CanBulkBuy.ShouldBeTrue();
        vm.BulkBuyText.ShouldStartWith("BUY MAX");
        vm.BulkBuyText.ShouldContain("(");
    }

    [Fact]
    public void Refresh_AllMilestonesReached_NoCash_BulkBuyDimsButStaysVisible()
    {
        var biz = MakeBusiness(owned: 1000);
        biz = biz with { BaseCost = 10, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(BigDouble.Zero, NoAngels);

        vm.HasNextMilestone.ShouldBeFalse();
        vm.CanBulkBuy.ShouldBeFalse();
        vm.BulkBuyText.ShouldBe("BUY MAX");
    }

    [Fact]
    public void Refresh_WithAngelBonus_ShouldMultiplyRevenueText()
    {
        var biz = MakeBusiness(owned: 1);
        biz = biz with { BaseRevenue = 100, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), angelBonus: new BigDouble(2.0));

        vm.RevenueText.ShouldBe("200.00");
    }

    [Fact]
    public void Refresh_WithAngelBonus_ShouldMultiplyRevenuePerSecondText()
    {
        var biz = MakeBusiness(owned: 1);
        biz = biz with { BaseRevenue = 60, BaseTimeSeconds = 2, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), angelBonus: new BigDouble(3.0));

        vm.RevenuePerSecondText.ShouldBe("$90.00/s");
    }

    [Fact]
    public void Refresh_NoAngelBonus_ShouldShowBaseRevenue()
    {
        var biz = MakeBusiness(owned: 1);
        biz = biz with { BaseRevenue = 100, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), angelBonus: BigDouble.One);

        vm.RevenueText.ShouldBe("100.00");
        vm.RevenuePerSecondText.ShouldBe("$100.00/s");
    }

    [Fact]
    public void Refresh_AngelBonusWithMilestones_ShouldStack()
    {
        var biz = MakeBusiness(owned: 25);
        biz = biz with { BaseRevenue = 1, CostMultiplier = 1.01 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), angelBonus: new BigDouble(4.0));

        vm.RevenueText.ShouldBe("200.00");
    }

    [Fact]
    public void Refresh_ZeroOwned_ShouldShowDashRegardlessOfAngelBonus()
    {
        var biz = MakeBusiness(owned: 0);
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1_000_000), angelBonus: new BigDouble(50.0));

        vm.RevenueText.ShouldBe("—");
        vm.RevenuePerSecondText.ShouldBe("—");
    }

    [Fact]
    public void Refresh_AtHugeCash_CostTextRemainsCompact()
    {
        var biz = MakeBusiness(owned: 5000);
        biz = biz with { BaseCost = 4, CostMultiplier = 1.07 };
        var (vm, _) = MakeVm(biz);

        vm.Refresh(new BigDouble(1.0, 500), NoAngels);

        vm.CostText.Length.ShouldBeLessThan(25);
        vm.CostText.ShouldNotContain("Infinity");
        vm.CostText.ShouldNotContain("NaN");
    }

    private static Business MakeBusiness(int owned = 0) => new()
    {
        Id = "test",
        Name = "Test",
        Icon = "T",
        Color = "#FFF",
        BaseCost = 100,
        BaseRevenue = 10,
        BaseTimeSeconds = 1,
        CostMultiplier = 1.1,
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
