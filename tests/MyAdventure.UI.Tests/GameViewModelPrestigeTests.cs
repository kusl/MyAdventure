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

public class GameViewModelPrestigeTests
{
    private static (GameViewModel vm, GameEngine engine) MakeVm()
    {
        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<GameState?>(null));
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var vm = new GameViewModel(engine, NullLogger<GameViewModel>.Instance, new ToastService());
        return (vm, engine);
    }

    private static void SetLifetime(GameEngine engine, BigDouble value) =>
        typeof(GameEngine).GetProperty(nameof(GameEngine.LifetimeEarnings))!
            .GetSetMethod(true)!.Invoke(engine, [value]);

    [Fact]
    public void PrestigeExplanation_ShowsTheIncomeMultiplierPrestigeWouldGrant()
    {
        var (vm, engine) = MakeVm();
        SetLifetime(engine, new BigDouble(1e16));

        vm.OnTick();

        vm.CanPrestige.ShouldBeTrue();
        vm.NextAngelText.ShouldBe("1.50 K");
        vm.PrestigeExplanation.ShouldContain("income \u00D731.00");
    }

    [Fact]
    public void PrestigeExplanation_BelowThreshold_AsksToKeepEarning()
    {
        var (vm, _) = MakeVm();

        vm.OnTick();

        vm.CanPrestige.ShouldBeFalse();
        vm.PrestigeExplanation.ShouldStartWith("Keep earning");
    }

    [Fact]
    public void CrossBonusTexts_AfterLastThreshold_ShowMaxedState()
    {
        var (vm, engine) = MakeVm();
        foreach (var biz in engine.Businesses) biz.Owned = 400;

        vm.OnTick();

        vm.HasCrossSpeedBonus.ShouldBeTrue();
        vm.CrossSpeedText.ShouldBe($"\u00D7{NumberFormatter.Format(new BigDouble(CrossBusinessSpeedBonus.MaxMultiplier))} Speed");
        vm.NextCrossThresholdText.ShouldBe("All cross-business bonuses earned");
    }

    [Fact]
    public void CrossBonusTexts_BeforeFirstThreshold_ShowRoadmap()
    {
        var (vm, _) = MakeVm();

        vm.OnTick();

        vm.HasCrossSpeedBonus.ShouldBeFalse();
        vm.NextCrossThresholdText.ShouldBe("25 more of every business \u2192 25");
    }
}
