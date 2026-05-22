using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Threading;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Android.Views;

public partial class MainView : UserControl
{
    private DispatcherTimer? _gameTimer;
    private IInsetsManager? _insets;

    public MainView()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Publish ourselves as the active top-level so platform services
        // (clipboard, etc.) can find us via AppRoot.CurrentVisual.
        AppRoot.CurrentVisual = this;

        // Safe-area handling: Android 15+ enforces edge-to-edge. Capture
        // the initial value at attach (SafeAreaChanged only fires on
        // subsequent changes) and apply it as Padding on this UserControl.
        var topLevel = TopLevel.GetTopLevel(this);
        _insets = topLevel?.InsetsManager;
        if (_insets is not null)
        {
            ApplySafeArea(_insets.SafeAreaPadding);
            _insets.SafeAreaChanged += OnSafeAreaChanged;
        }

        if (DataContext is GameViewModel vm)
        {
            await vm.InitializeAsync();

            _gameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _gameTimer.Tick += (_, _) => vm.OnTick();
            _gameTimer.Start();
        }
    }

    protected override async void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gameTimer?.Stop();
        if (DataContext is GameViewModel vm)
            await vm.SaveAsync();

        if (_insets is not null)
        {
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
            _insets = null;
        }

        // Only clear AppRoot if it was pointing at us — a fresh MainView
        // may have already attached and overwritten it.
        if (ReferenceEquals(AppRoot.CurrentVisual, this))
            AppRoot.CurrentVisual = null;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) =>
        ApplySafeArea(e.SafeAreaPadding);

    private void ApplySafeArea(Thickness safeArea) =>
        Padding = safeArea;
}
