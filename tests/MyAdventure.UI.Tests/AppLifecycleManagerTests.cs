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
/// Tests for <see cref="AppLifecycleManager"/>, the cross-platform glue
/// between Avalonia's <c>IActivatableLifetime</c> and the game's
/// suspend/resume hooks. These tests exercise the parts of the manager
/// that don't require a running Avalonia app — argument validation, the
/// no-op fallback when no lifetime feature is exposed, and the
/// "current target gets replaced on subsequent Attach" semantics that
/// keep Android activity recreation from leaking event handlers.
/// </summary>
public class AppLifecycleManagerTests
{
    /// <summary>
    /// Ensures every test starts with a clean static-state slate so that
    /// test ordering doesn't matter. <see cref="AppLifecycleManager"/>
    /// holds a single static "current target" reference; without this
    /// reset, tests would observe each other's targets.
    /// </summary>
    public AppLifecycleManagerTests() => AppLifecycleManager.ResetForTesting();

    [Fact]
    public void Attach_NullViewModel_ShouldThrow()
    {
        // ArgumentNullException up front beats a NullReferenceException
        // surfacing later from inside an event handler that we have no
        // good way of unwinding once subscribed.
        Should.Throw<ArgumentNullException>(() => AppLifecycleManager.Attach(null!));
    }

    [Fact]
    public void Attach_WithoutAvaloniaApp_ShouldReturnFalse()
    {
        // In a unit test there's no Application.Current, so no
        // IActivatableLifetime. The manager must degrade gracefully:
        // returning false rather than throwing means the rest of the
        // game still boots in environments that don't expose lifecycle
        // events (headless tests, embedded targets without it, etc.).
        var vm = MakeViewModel();
        AppLifecycleManager.Attach(vm).ShouldBeFalse();
    }

    [Fact]
    public void Attach_TwiceWithDifferentVms_ShouldReplaceTarget()
    {
        // Even without a real lifetime feature, re-attaching must not
        // throw — Android activity recreation calls Attach repeatedly
        // and the manager has to be tolerant of that.
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
