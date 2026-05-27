using Avalonia;
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

            // Flush telemetry whenever the app goes to background.
            //
            // Android can kill a backgrounded process at any moment without
            // warning, so any unflushed OpenTelemetry batches just vanish.
            // The first thing the OS does before killing is push the app to
            // background, which raises IActivatableLifetime.Deactivated with
            // ActivationKind.Background — same event AppLifecycleManager
            // already uses for offline-earnings bookkeeping. Adding our
            // subscriber alongside is safe; the two handlers don't share
            // state and Avalonia delivers the event to both.
            //
            // Soft mode here, NOT Final. Disposing the ServiceProvider
            // on background would leave a dead container behind if the
            // user resumes the activity — and Android resume is the
            // common case, not the exception. Soft mode synchronously
            // flushes the trace provider's batch and lets the logger
            // pipeline keep running on its own 1-second timer (which
            // is fast enough that the next backgrounding will catch
            // anything emitted after this flush completes).
            //
            // We subscribe directly to IActivatableLifetime here rather
            // than going through AppLifecycleManager because telemetry
            // flushing is an app-lifetime concern, not a game-state
            // concern, and folding it into AppLifecycleManager would
            // muddle the responsibilities of a service whose only job
            // is offline earnings.
            var lifetime = this.TryGetFeature<IActivatableLifetime>();
            if (lifetime is not null)
            {
                lifetime.Deactivated += (_, e) =>
                {
                    // Filter to Background events specifically; Avalonia
                    // also raises Deactivated for things like dialog
                    // focus changes (ActivationKind.Application) where
                    // a telemetry flush would be wasted overhead.
                    if (e.Kind != ActivationKind.Background) return;
                    if (Services is null) return;

                    try
                    {
                        DependencyInjection
                            .FlushTelemetryAsync(Services, DependencyInjection.TelemetryFlushMode.Soft)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch
                    {
                        // FlushTelemetryAsync swallows already; this
                        // extra guard exists to protect against any
                        // GetAwaiter()-thrown wrapping we might pick up
                        // from future runtime changes.
                    }
                };
            }
            else
            {
                global::Android.Util.Log.Warn(Tag,
                    "IActivatableLifetime not available; telemetry will not be flushed on background.");
            }

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
