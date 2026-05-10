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
        // (clipboard, etc.) can find us via AppRoot.CurrentVisual. On
        // Android in Avalonia 12, IActivityApplicationLifetime no longer
        // exposes a live MainView reference — only a MainViewFactory —
        // so the View itself is responsible for announcing its presence.
        AppRoot.CurrentVisual = this;

        // ---- Safe-area handling --------------------------------------
        //
        // Android 15+ enforces edge-to-edge: without explicit handling
        // our top bar gets drawn under the status bar / front-camera
        // cutout, and the first row of business cards visually rides on
        // top of the prestige bar. We've turned off Avalonia's automatic
        // safe-area injection on the UserControl (TopLevel.AutoSafeAreaPadding="False")
        // and apply it ourselves here, so it lands on this exact control's
        // Padding rather than getting silently absorbed by an ancestor.
        //
        // Capture the initial value at attach time — SafeAreaChanged only
        // fires on subsequent changes, so without this the very first
        // frame would render with no padding.
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

            // OnAttachedToVisualTree runs on the UI thread, which (in
            // Avalonia 12) is the dispatcher DispatcherTimer will bind to.
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

        // Only clear AppRoot if it was pointing at us. Android can
        // recreate activities aggressively, and a fresh MainView may have
        // already attached and overwritten AppRoot.CurrentVisual before
        // this older view detaches — clearing it unconditionally would
        // leave the new view stranded.
        if (ReferenceEquals(AppRoot.CurrentVisual, this))
            AppRoot.CurrentVisual = null;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) =>
        ApplySafeArea(e.SafeAreaPadding);

    /// <summary>
    /// Apply the OS-reported safe-area as Padding on this UserControl.
    /// The DockPanel inside us has a small visual margin on top of this,
    /// which is fine — the padding here only protects against the system
    /// bars / cutout, the inner margin is purely cosmetic.
    /// </summary>
    private void ApplySafeArea(Thickness safeArea) =>
        Padding = safeArea;
}
