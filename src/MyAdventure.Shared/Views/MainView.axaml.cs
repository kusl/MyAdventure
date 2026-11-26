using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Threading;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Shared.Views;

public partial class MainView : UserControl
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private DispatcherTimer? _loop;
    private IInsetsManager? _insets;

    public MainView()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _insets = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (_insets is not null)
        {
            Padding = _insets.SafeAreaPadding;
            _insets.SafeAreaChanged += OnSafeAreaChanged;
        }

        if (DataContext is not GameViewModel vm) return;

        vm.Clipboard = new TopLevelClipboard(this);
        await vm.InitializeAsync();

        if (_loop is not null || TopLevel.GetTopLevel(this) is null) return;
        _loop = new DispatcherTimer { Interval = FrameInterval };
        _loop.Tick += (_, _) => vm.OnTick();
        _loop.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _loop?.Stop();
        _loop = null;

        if (_insets is not null)
        {
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
            _insets = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => Padding = e.SafeAreaPadding;
}
