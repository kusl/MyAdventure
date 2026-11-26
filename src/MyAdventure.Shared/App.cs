using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Telemetry;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;
using MyAdventure.Shared.Views;

namespace MyAdventure.Shared;

public class App : Application
{
    private readonly LifecycleRouter _lifecycle = new();
    private ServiceProvider? _services;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new Fluent2Theme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigurePlatformServices(services);
        services.AddInfrastructure(TelemetryConfigurationLoader.Load()).AddGame();
        var provider = services.BuildServiceProvider();
        _services = provider;

        TelemetryLifecycle.Start(provider);
        ObserveUnhandledExceptions(provider.GetRequiredService<ILoggerFactory>().CreateLogger<App>());
        ObserveLifecycle(provider);

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow(CreateMainView(provider));
                desktop.Exit += (_, _) => Shutdown(provider);
                break;
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = () => CreateMainView(provider);
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = CreateMainView(provider);
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    protected virtual void ConfigurePlatformServices(IServiceCollection services)
    {
    }

    private Control CreateMainView(IServiceProvider provider)
    {
        var viewModel = provider.GetRequiredService<GameViewModel>();
        _lifecycle.Attach(viewModel);
        return new MainView { DataContext = viewModel };
    }

    private void ObserveLifecycle(IServiceProvider provider)
    {
        _lifecycle.Backgrounded += (_, _) => _ = TelemetryLifecycle.FlushAsync(provider, TelemetryFlushMode.Soft);

        if (this.TryGetFeature<IActivatableLifetime>() is not { } activatable) return;
        activatable.Activated += (_, e) => _lifecycle.OnActivated(e.Kind);
        activatable.Deactivated += (_, e) => _lifecycle.OnDeactivated(e.Kind);
    }

    private void Shutdown(IServiceProvider provider)
    {
        if (_services is null) return;
        _services = null;

        var viewModel = provider.GetRequiredService<GameViewModel>();
        Task.Run(() => viewModel.SaveAsync()).GetAwaiter().GetResult();
        TelemetryLifecycle.FlushAsync(provider, TelemetryFlushMode.Final).GetAwaiter().GetResult();
    }

    private static void ObserveUnhandledExceptions(ILogger logger)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }
}
