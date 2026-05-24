using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Core.Services;
using MyAdventure.Desktop.Views;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Telemetry;
using MyAdventure.Shared.Services;
using MyAdventure.Shared.ViewModels;

namespace MyAdventure.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // Load appsettings.json (copied to the output directory by the
        // csproj's <None Include="appsettings.json" CopyToOutputDirectory />
        // rule) and merge environment-variable overrides on top. The
        // result is a TelemetryOptions instance that drives whether
        // Sentry's OTLP exporters are registered. Defaults are completely
        // safe — Sentry off, console-only logging — so the first build
        // after a fresh checkout works without any configuration at all.
        //
        // appsettings.local.json is honoured for developer overrides
        // (e.g. a personal Sentry DSN) and is gitignored. It does not
        // need to exist; the optional flag keeps startup clean when
        // there is no override file.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var telemetry = TelemetryConfigurationLoader.LoadFromConfiguration(configuration);

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry);
        services.AddSingleton<ToastService>();
        services.AddTransient<GameEngine>();
        services.AddTransient<GameViewModel>();
        services.AddSingleton<IConfiguration>(configuration);
        Services = services.BuildServiceProvider();

        DependencyInjection.EmitStartupBreadcrumb(Services);
        await DependencyInjection.InitializeDatabaseAsync(Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<GameViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            AppLifecycleManager.Attach(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
