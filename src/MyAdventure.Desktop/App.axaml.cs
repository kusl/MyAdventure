using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Core.Services;
using MyAdventure.Desktop.Views;
using MyAdventure.Infrastructure;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Desktop;

public partial class App : Avalonia.Application
{
    private IServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<ToastService>();
        services.AddTransient<GameEngine>();
        services.AddTransient<GameViewModel>();
        _services = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(_services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = _services.GetRequiredService<GameViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
