using Avalonia.Controls.ApplicationLifetimes;
using MyAdventure.Shared.Services;
using NSubstitute;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class LifecycleRouterTests
{
    private readonly LifecycleRouter _router = new();
    private readonly ILifecycleAware _target = Substitute.For<ILifecycleAware>();

    [Fact]
    public void Attach_Null_Throws() => Should.Throw<ArgumentNullException>(() => _router.Attach(null!));

    [Fact]
    public void BackgroundDeactivation_SuspendsTargetAndRaisesBackgrounded()
    {
        var backgrounded = 0;
        _router.Backgrounded += (_, _) => backgrounded++;
        _router.Attach(_target);

        _router.OnDeactivated(ActivationKind.Background);

        _target.Received(1).OnSuspended();
        backgrounded.ShouldBe(1);
    }

    [Fact]
    public void BackgroundActivation_ResumesTarget()
    {
        _router.Attach(_target);

        _router.OnActivated(ActivationKind.Background);

        _target.Received(1).OnResumed();
    }

    [Theory]
    [InlineData(ActivationKind.File)]
    [InlineData(ActivationKind.OpenUri)]
    public void NonBackgroundEvents_AreIgnored(ActivationKind kind)
    {
        _router.Attach(_target);

        _router.OnActivated(kind);
        _router.OnDeactivated(kind);

        _target.DidNotReceive().OnResumed();
        _target.DidNotReceive().OnSuspended();
    }

    [Fact]
    public void Attach_ReplacesPreviousTarget()
    {
        var replacement = Substitute.For<ILifecycleAware>();
        _router.Attach(_target);
        _router.Attach(replacement);

        _router.OnDeactivated(ActivationKind.Background);

        _target.DidNotReceive().OnSuspended();
        replacement.Received(1).OnSuspended();
    }

    [Fact]
    public void Detach_OnlyRemovesMatchingTarget()
    {
        var other = Substitute.For<ILifecycleAware>();
        _router.Attach(_target);

        _router.Detach(other);
        _router.OnActivated(ActivationKind.Background);
        _target.Received(1).OnResumed();

        _router.Detach(_target);
        _router.OnActivated(ActivationKind.Background);
        _target.Received(1).OnResumed();
    }
}
