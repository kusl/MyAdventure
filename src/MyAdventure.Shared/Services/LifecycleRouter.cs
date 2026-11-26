using Avalonia.Controls.ApplicationLifetimes;

namespace MyAdventure.Shared.Services;

public interface ILifecycleAware
{
    void OnSuspended();

    void OnResumed();
}

public sealed class LifecycleRouter
{
    private ILifecycleAware? _target;

    public event EventHandler? Backgrounded;

    public void Attach(ILifecycleAware target) => _target = target ?? throw new ArgumentNullException(nameof(target));

    public void Detach(ILifecycleAware target)
    {
        if (ReferenceEquals(_target, target)) _target = null;
    }

    public void OnActivated(ActivationKind kind)
    {
        if (kind == ActivationKind.Background) _target?.OnResumed();
    }

    public void OnDeactivated(ActivationKind kind)
    {
        if (kind != ActivationKind.Background) return;
        _target?.OnSuspended();
        Backgrounded?.Invoke(this, EventArgs.Empty);
    }
}
