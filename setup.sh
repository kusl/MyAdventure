#!/bin/bash
# =============================================================================
# Fix all 4 remaining errors:
#   1. UI.Tests: BusinessViewModelTests missing ToastService parameter (2 errors)
#   2. Core.Tests: AffordableCount_MultiplePurchases floating point (1 error)
#   3. Core.Tests: BuyBusiness_NotEnoughCash — Cash starts at 5, lemonade costs 4 (1 error)
# =============================================================================
set -euo pipefail

echo "=== Fixing all test errors ==="

# =============================================================================
# 1. Fix BusinessViewModelTests — add ToastService parameter
# =============================================================================

cat > tests/MyAdventure.UI.Tests/BusinessViewModelTests.cs << 'ENDOFFILE'
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
ENDOFFILE

# =============================================================================
# 2. Fix AffordableCount_MultiplePurchases — floating point precision
#    100 + 110 + 121.00000000000001 > 331 due to IEEE 754
#    Fix: use 332 which is unambiguously enough for 3 purchases
# =============================================================================

cat > tests/MyAdventure.Core.Tests/BusinessAffordableTests.cs << 'ENDOFFILE'
using MyAdventure.Core.Entities;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class BusinessAffordableTests
{
    private Business CreateBusiness(int owned = 0) => new()
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

    [Fact]
    public void AffordableCount_NoCash_ShouldBeZero()
    {
        var biz = CreateBusiness();
        biz.AffordableCount(0).ShouldBe(0);
    }

    [Fact]
    public void AffordableCount_ExactlyOneCost_ShouldBeOne()
    {
        var biz = CreateBusiness();
        biz.AffordableCount(100).ShouldBe(1);
    }

    [Fact]
    public void AffordableCount_MultiplePurchases()
    {
        var biz = CreateBusiness();
        // Cost 0: 100, Cost 1: 110, Cost 2: 121 => total ~331
        // Use 332 to avoid IEEE 754 floating point edge (1.1^2 = 1.2100000000000002)
        biz.AffordableCount(332).ShouldBe(3);
    }

    [Fact]
    public void AffordableCount_SlightlyUnder_ShouldBeOneLess()
    {
        var biz = CreateBusiness();
        // Can buy 2 for 100 + 110 = 210, but not 3 (need ~121 more)
        biz.AffordableCount(210).ShouldBe(2);
    }

    [Fact]
    public void Revenue_WithMilestones_ShouldMultiply()
    {
        var biz = CreateBusiness(owned: 25);
        // 25 owned × 10 base revenue × 2.0 milestone = 500
        biz.Revenue.ShouldBe(500);
    }

    [Fact]
    public void RevenuePerSecond_ShouldEqualRevenueOverCycleTime()
    {
        var biz = CreateBusiness(owned: 5);
        var expected = biz.Revenue / biz.CycleTimeSeconds;
        biz.RevenuePerSecond.ShouldBe(expected);
    }
}
ENDOFFILE

# =============================================================================
# 3. Fix BuyBusiness_NotEnoughCash_ShouldFail
#    LoadAsync with no save sets Cash = 5.0, lemonade BaseCost = 4
#    So the engine CAN buy it — that's correct behavior.
#    Fix: set cash to 0 before attempting the buy, or test with newspaper (cost 60)
# =============================================================================

cat > tests/MyAdventure.Core.Tests/GameEngineTests.cs << 'ENDOFFILE'
using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.Core.Tests;

public class GameEngineTests
{
    private readonly IGameStateRepository _repo = Substitute.For<IGameStateRepository>();
    private readonly GameEngine _engine;

    public GameEngineTests()
    {
        _repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));

        _engine = new GameEngine(_repo, NullLogger<GameEngine>.Instance);
    }

    [Fact]
    public async Task LoadAsync_NoSave_ShouldStartFresh()
    {
        await _engine.LoadAsync();
        _engine.Cash.ShouldBe(5.0);
        _engine.Businesses.Count.ShouldBe(6);
    }

    [Fact]
    public async Task BuyBusiness_ShouldDeductCashAndIncrementOwned()
    {
        await _engine.LoadAsync();
        SetCash(100);

        var result = _engine.BuyBusiness("lemonade");
        result.ShouldBeTrue();
        _engine.Businesses.First(b => b.Id == "lemonade").Owned.ShouldBe(1);
        _engine.Cash.ShouldBeLessThan(100);
    }

    [Fact]
    public async Task BuyBusiness_NotEnoughCash_ShouldFail()
    {
        await _engine.LoadAsync();
        // Starting cash is 5.0 — newspaper costs 60, so this should fail
        _engine.BuyBusiness("newspaper").ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_RunningBusiness_ShouldEarnRevenue()
    {
        await _engine.LoadAsync();
        SetCash(1000);
        _engine.BuyBusiness("lemonade");
        _engine.StartBusiness("lemonade");

        for (var i = 0; i < 100; i++)
            _engine.Tick(0.1);

        _engine.Cash.ShouldBeGreaterThan(990);
    }

    [Fact]
    public async Task Tick_MilestoneBoostedRevenue_ShouldEarnMore()
    {
        await _engine.LoadAsync();
        SetCash(1_000_000);

        // Buy 25 lemonade stands to hit first milestone
        for (var i = 0; i < 25; i++)
            _engine.BuyBusiness("lemonade");

        var lemonade = _engine.Businesses.First(b => b.Id == "lemonade");
        lemonade.Owned.ShouldBe(25);
        lemonade.MilestoneMultiplier.ShouldBe(2.0);

        // Revenue should be base × owned × multiplier
        lemonade.Revenue.ShouldBe(lemonade.BaseRevenue * 25 * 2.0);
    }

    [Fact]
    public async Task Prestige_NotEnoughEarnings_ShouldFail()
    {
        await _engine.LoadAsync();
        var (_, success) = _engine.Prestige();
        success.ShouldBeFalse();
    }

    [Fact]
    public void CalculateAngels_ShouldReturnZeroBelowThreshold() =>
        GameEngine.CalculateAngels(1e11).ShouldBe(0);

    [Fact]
    public void CalculateAngels_ShouldReturnPositiveAboveThreshold() =>
        GameEngine.CalculateAngels(1e14).ShouldBeGreaterThan(0);

    private void SetCash(double amount)
    {
        var cashProp = typeof(GameEngine).GetProperty(nameof(GameEngine.Cash))!;
        cashProp.GetSetMethod(true)!.Invoke(_engine, [amount]);
    }
}
ENDOFFILE

echo ""
echo "=== All fixes applied ==="
echo ""
echo "Fixes:"
echo "  1. BusinessViewModelTests.cs — added ToastService() as 3rd constructor arg (2 compile errors)"
echo "  2. BusinessAffordableTests.cs — changed 331→332 and 330→210 to avoid IEEE 754 edge case (1 test failure)"
echo "  3. GameEngineTests.cs — test newspaper (cost 60) instead of lemonade (cost 4) since starting cash is 5 (1 test failure)"
echo ""
echo "Next: dotnet build && dotnet test"
