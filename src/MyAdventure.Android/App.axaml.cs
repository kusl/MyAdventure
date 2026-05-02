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

            // Avalonia 12: Android uses IActivityApplicationLifetime with a
            // MainViewFactory (a Func<Control>) instead of ISingleViewApplicationLifetime
            // with a single MainView reference. Android can recreate the activity
            // multiple times during the app's lifetime — the factory is invoked
            // for each fresh activity, producing a fresh view + fresh ViewModel
            // that re-loads from the database.
            if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                activityLifetime.MainViewFactory = () =>
                {
                    var vm = Services!.GetRequiredService<GameViewModel>();

                    // Wire the cross-platform suspend/resume signal. We
                    // call this inside the factory because each activity
                    // recreation produces a fresh VM, and AppLifecycleManager
                    // holds a single static "current target" reference —
                    // each Attach replaces the previous target so old VMs
                    // stop receiving events. See AppLifecycleManager for
                    // the full rationale.
                    AppLifecycleManager.Attach(vm);

                    return new MainView { DataContext = vm };
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                // Fallback for any non-Android single-view platforms (browser, iOS)
                // in case this same App class is ever reused there. Android will
                // never hit this branch in v12.
                var vm = Services.GetRequiredService<GameViewModel>();
                singleView.MainView = new MainView { DataContext = vm };
                AppLifecycleManager.Attach(vm);
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
