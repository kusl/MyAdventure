using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Android.Views;
using MyAdventure.Core.Services;
using MyAdventure.Infrastructure;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Android;

public partial class App : Avalonia.Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddTransient<GameEngine>();
        services.AddTransient<GameViewModel>();
        Services = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(Services);

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var vm = Services.GetRequiredService<GameViewModel>();
            singleView.MainView = new MainView { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
