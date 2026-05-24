using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Android.Views;
using MyAdventure.Core.Services;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Telemetry;
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

            // Android does not ship with the typical .NET host-bootstrapping
            // pipeline that auto-binds appsettings.json. Instead we read
            // telemetry config from environment variables — toggling
            // Sentry on/off for the APK is a matter of setting SENTRY_DSN
            // (e.g. via `adb shell setprop` during testing, or by burning
            // it into the build via an AndroidEnvironment file for
            // production builds).
            var telemetry = TelemetryConfigurationLoader.LoadFromEnvironment();

            var services = new ServiceCollection();
            services.AddInfrastructure(telemetry);
            services.AddSingleton<ToastService>();
            services.AddTransient<GameEngine>();
            services.AddTransient<GameViewModel>();
            Services = services.BuildServiceProvider();

            DependencyInjection.EmitStartupBreadcrumb(Services);
            await DependencyInjection.InitializeDatabaseAsync(Services);

            // Avalonia 12: Android uses IActivityApplicationLifetime with
            // a MainViewFactory. The factory is invoked for each fresh
            // activity, producing a fresh view + fresh ViewModel that
            // re-loads from the database.
            if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                activityLifetime.MainViewFactory = () =>
                {
                    var vm = Services!.GetRequiredService<GameViewModel>();

                    // Replace any previous AppLifecycleManager target so
                    // old VMs stop receiving events.
                    AppLifecycleManager.Attach(vm);

                    return new MainView { DataContext = vm };
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                // Fallback for any non-Android single-view platforms.
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
