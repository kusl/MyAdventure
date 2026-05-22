using Avalonia.Controls;
using Avalonia.Threading;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Desktop.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _gameTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Publish ourselves as the active top-level so platform services
        // (clipboard, etc.) can find us via AppRoot.CurrentVisual without
        // needing to walk the application lifetime.
        AppRoot.CurrentVisual = this;

        if (DataContext is GameViewModel vm)
        {
            await vm.InitializeAsync();

            // Avalonia 12: DispatcherTimer binds to the current dispatcher
            // at construction. OnOpened runs on the UI thread, so we get
            // the UI dispatcher here, which is what we want.
            _gameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _gameTimer.Tick += (_, _) => vm.OnTick();
            _gameTimer.Start();
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        _gameTimer?.Stop();

        if (DataContext is GameViewModel vm)
            await vm.SaveAsync();

        if (ReferenceEquals(AppRoot.CurrentVisual, this))
            AppRoot.CurrentVisual = null;

        base.OnClosing(e);
    }
}
