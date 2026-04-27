using Avalonia.Controls;
using Avalonia.Threading;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Android.Views;

public partial class MainView : UserControl
{
    private DispatcherTimer? _gameTimer;

    public MainView()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Publish ourselves as the active top-level so platform services
        // (clipboard, etc.) can find us via AppRoot.CurrentVisual. On
        // Android in Avalonia 12, IActivityApplicationLifetime no longer
        // exposes a live MainView reference — only a MainViewFactory —
        // so the View itself is responsible for announcing its presence.
        AppRoot.CurrentVisual = this;

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

    protected override async void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _gameTimer?.Stop();
        if (DataContext is GameViewModel vm)
            await vm.SaveAsync();

        // Only clear AppRoot if it was pointing at us. Android can
        // recreate activities aggressively, and a fresh MainView may have
        // already attached and overwritten AppRoot.CurrentVisual before
        // this older view detaches — clearing it unconditionally would
        // leave the new view stranded.
        if (ReferenceEquals(AppRoot.CurrentVisual, this))
            AppRoot.CurrentVisual = null;

        base.OnDetachedFromVisualTree(e);
    }
}
