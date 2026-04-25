using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class BusinessViewModelTests
{
    private const double NoAngels = 1.0;

    [Fact]
    public void Refresh_ShouldUpdateAllProperties()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test Biz",
            Icon = "🧪",
            Color = "#FF0000",
            BaseCost = 100,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1,
            Owned = 3
        };

        var repo = Substitute.For<IGameStateRepository>();
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        var vm = new BusinessViewModel(biz, engine, toasts);

        vm.Refresh(500, NoAngels);

        vm.Owned.ShouldBe(3);
        vm.CostText.ShouldNotBeNullOrEmpty();
        vm.RevenueText.ShouldNotBe("—");
        vm.CanAfford.ShouldBeTrue();
    }

    [Fact]
    public void Refresh_NotEnoughCash_ShouldShowNotAffordable()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 1000,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1
        };

        var repo = Substitute.For<IGameStateRepository>();
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        var vm = new BusinessViewModel(biz, engine, toasts);

        vm.Refresh(5, NoAngels);

        vm.CanAfford.ShouldBeFalse();
    }

    [Fact]
    public void Refresh_ShouldShowBuyToNextMilestoneText()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 10,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.05,
            Owned = 20
        };

        var repo = Substitute.For<IGameStateRepository>();
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        var vm = new BusinessViewModel(biz, engine, toasts);

        vm.Refresh(10_000, NoAngels);

        // 20 owned, next milestone is 25, so 5 more needed
        vm.HasNextMilestone.ShouldBeTrue();
        vm.BuyToNextMilestoneText.ShouldBe("BUY 5→25");
        vm.CanBuyToNextMilestone.ShouldBeTrue();
    }

    [Fact]
    public void Refresh_AllMilestonesReached_ShouldHideBuyToMilestone()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 10,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.01,
            Owned = 1000
        };

        var repo = Substitute.For<IGameStateRepository>();
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        var vm = new BusinessViewModel(biz, engine, toasts);

        vm.Refresh(10_000, NoAngels);

        vm.HasNextMilestone.ShouldBeFalse();
        vm.CanBuyToNextMilestone.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Bug-2 regression coverage: angel multiplier must reach the UI.
    // ---------------------------------------------------------------
    [Fact]
    public void Refresh_WithAngelBonus_ShouldMultiplyRevenueText()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 10,
            BaseRevenue = 100,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1,
            Owned = 1
        };

        var (vm, _) = MakeVm(biz);

        // Pre-bonus revenue would be 1 × 100 × 1.0 = 100.00
        // With 2.0× angel bonus: 200.00
        vm.Refresh(1_000_000, angelBonus: 2.0);

        vm.RevenueText.ShouldBe("200.00");
    }

    [Fact]
    public void Refresh_WithAngelBonus_ShouldMultiplyRevenuePerSecondText()
    {
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 10,
            BaseRevenue = 60,
            BaseTimeSeconds = 2, // 60 / 2 = 30/s pre-bonus
            CostMultiplier = 1.1,
            Owned = 1
        };

        var (vm, _) = MakeVm(biz);

        vm.Refresh(1_000_000, angelBonus: 3.0);

        // 60 (revenue) / 2 (cycle) × 3.0 (angels) = 90/s
        vm.RevenuePerSecondText.ShouldBe("$90.00/s");
    }

    [Fact]
    public void Refresh_NoAngelBonus_ShouldShowBaseRevenue()
    {
        // Inverse check: 1.0× must produce identical text to no multiplication.
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 10,
            BaseRevenue = 100,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1,
            Owned = 1
        };

        var (vm, _) = MakeVm(biz);

        vm.Refresh(1_000_000, angelBonus: 1.0);

        vm.RevenueText.ShouldBe("100.00");
        vm.RevenuePerSecondText.ShouldBe("$100.00/s");
    }

    [Fact]
    public void Refresh_AngelBonusWithMilestones_ShouldStack()
    {
        // Bonuses compound: milestone multiplier × angel bonus.
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 10,
            BaseRevenue = 1,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.01,
            Owned = 25 // ×2 milestone
        };

        var (vm, _) = MakeVm(biz);

        vm.Refresh(1_000_000, angelBonus: 4.0);

        // 25 owned × $1 base × 2.0 milestone × 4.0 angels = $200
        vm.RevenueText.ShouldBe("200.00");
    }

    [Fact]
    public void Refresh_ZeroOwned_ShouldShowDashRegardlessOfAngelBonus()
    {
        // Angel bonus must not turn an unowned business into a "—" with a number.
        var biz = new Business
        {
            Id = "test",
            Name = "Test",
            Icon = "T",
            Color = "#FFF",
            BaseCost = 100,
            BaseRevenue = 10,
            BaseTimeSeconds = 1,
            CostMultiplier = 1.1,
            Owned = 0
        };

        var (vm, _) = MakeVm(biz);

        vm.Refresh(1_000_000, angelBonus: 50.0);

        vm.RevenueText.ShouldBe("—");
        vm.RevenuePerSecondText.ShouldBe("—");
    }

    private static (BusinessViewModel vm, GameEngine engine) MakeVm(Business biz)
    {
        var repo = Substitute.For<IGameStateRepository>();
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        var vm = new BusinessViewModel(biz, engine, toasts);
        return (vm, engine);
    }
}
