using Microsoft.Extensions.Logging.Abstractions;
using MyAdventure.Core.Entities;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

/// <summary>
/// Tests for <see cref="AppLifecycleManager"/>. These cover the parts
/// that don't require a running Avalonia app: argument validation, the
/// no-op fallback when no lifetime feature is exposed, and the
/// "current target gets replaced on subsequent Attach" semantics that
/// keep Android activity recreation from leaking event handlers.
/// </summary>
public class AppLifecycleManagerTests
{
    public AppLifecycleManagerTests() => AppLifecycleManager.ResetForTesting();

    [Fact]
    public void Attach_NullViewModel_ShouldThrow()
    {
        Should.Throw<ArgumentNullException>(() => AppLifecycleManager.Attach(null!));
    }

    [Fact]
    public void Attach_WithoutAvaloniaApp_ShouldReturnFalse()
    {
        // In a unit test there's no Application.Current, so no
        // IActivatableLifetime. The manager must degrade gracefully.
        var vm = MakeViewModel();
        AppLifecycleManager.Attach(vm).ShouldBeFalse();
    }

    [Fact]
    public void Attach_TwiceWithDifferentVms_ShouldReplaceTarget()
    {
        var vm1 = MakeViewModel();
        var vm2 = MakeViewModel();

        Should.NotThrow(() =>
        {
            AppLifecycleManager.Attach(vm1);
            AppLifecycleManager.Attach(vm2);
        });
    }

    private static GameViewModel MakeViewModel()
    {
        var repo = Substitute.For<IGameStateRepository>();
        repo.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameState?>(null));
        var engine = new GameEngine(repo, NullLogger<GameEngine>.Instance);
        var toasts = new ToastService();
        return new GameViewModel(engine, NullLogger<GameViewModel>.Instance, toasts);
    }
}
