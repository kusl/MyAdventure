using Avalonia.Controls;
using Avalonia.Threading;
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

    protected override async void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _gameTimer?.Stop();
        if (DataContext is GameViewModel vm)
            await vm.SaveAsync();
        base.OnDetachedFromVisualTree(e);
    }
}
