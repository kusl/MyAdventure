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

        vm.Refresh(500);

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

        vm.Refresh(5);

        vm.CanAfford.ShouldBeFalse();
    }
}
