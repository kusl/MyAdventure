using Avalonia.Controls;

namespace MyAdventure.Shared.Views;

public sealed class MainWindow : Window
{
    public MainWindow(Control content)
    {
        Title = "MyAdventure";
        Width = 1100;
        Height = 750;
        MinWidth = 360;
        MinHeight = 560;
        Content = content;
    }
}
