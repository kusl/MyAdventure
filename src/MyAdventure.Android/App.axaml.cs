using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Android.Views;
using MyAdventure.Core.Services;
using MyAdventure.Infrastructure;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Android;

public partial class App : Avalonia.Application
{
    private const string Tag = "MyAdventure";

    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        global::Android.Util.Log.Info(Tag, "App.Initialize() starting");
        AvaloniaXamlLoader.Load(this);
        global::Android.Util.Log.Info(Tag, "App.Initialize() done");
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        try
        {
            global::Android.Util.Log.Info(Tag, "OnFrameworkInitializationCompleted starting");

            var services = new ServiceCollection();
            services.AddInfrastructure();
            services.AddSingleton<ToastService>();
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
            global::Android.Util.Log.Info(Tag, "OnFrameworkInitializationCompleted done");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(Tag, $"FATAL during startup: {ex}");
            global::Android.Util.Log.Error(Tag, $"Inner: {ex.InnerException}");
            throw;
        }
    }
}
