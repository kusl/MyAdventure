using Avalonia.Controls;
using Avalonia.Threading;
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

        if (DataContext is GameViewModel vm)
        {
            await vm.InitializeAsync();

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

        base.OnClosing(e);
    }
}
