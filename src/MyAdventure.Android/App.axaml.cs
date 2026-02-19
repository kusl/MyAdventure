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

            global::Android.Util.Log.Info(Tag, "Adding infrastructure services...");
            services.AddInfrastructure();

            services.AddTransient<GameEngine>();
            services.AddTransient<GameViewModel>();
            Services = services.BuildServiceProvider();
            global::Android.Util.Log.Info(Tag, "ServiceProvider built");

            global::Android.Util.Log.Info(Tag, "Initializing database...");
            await DependencyInjection.InitializeDatabaseAsync(Services);
            global::Android.Util.Log.Info(Tag, "Database initialized");

            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                global::Android.Util.Log.Info(Tag, "Creating GameViewModel...");
                var vm = Services.GetRequiredService<GameViewModel>();

                global::Android.Util.Log.Info(Tag, "Creating MainView...");
                singleView.MainView = new MainView { DataContext = vm };
                global::Android.Util.Log.Info(Tag, "MainView assigned");
            }

            base.OnFrameworkInitializationCompleted();
            global::Android.Util.Log.Info(Tag, "OnFrameworkInitializationCompleted done");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(Tag, $"FATAL during startup: {ex}");
            global::Android.Util.Log.Error(Tag, $"Inner: {ex.InnerException}");
            // Re-throw so the OS reports the crash properly
            throw;
        }
    }
}
