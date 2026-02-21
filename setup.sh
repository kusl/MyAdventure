#!/bin/bash
# =============================================================================
# Fix: ToastService dependency chain
# - Remove ToastService registration from Infrastructure (avoids circular dep)
# - Register ToastService in Desktop/Android App.axaml.cs instead
# - Move ToastServiceTests to UI.Tests (which already references Shared)
# - Add Shared project reference to Core.Tests for future ViewModel tests
# =============================================================================
set -euo pipefail

echo "=== Fixing dependency chain ==="

# =============================================================================
# 1. Remove ToastService registration from Infrastructure/DependencyInjection.cs
# =============================================================================

cat > src/MyAdventure.Infrastructure/DependencyInjection.cs << 'ENDOFFILE'
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? dbPath = null)
    {
        dbPath ??= GetDefaultDbPath();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<IGameStateRepository, GameStateRepository>();

        // OpenTelemetry
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService("MyAdventure", "1.0.0");

        services.AddLogging(logging =>
            logging.AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(resourceBuilder);
                otel.AddConsoleExporter();
            }));

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                .AddSource("MyAdventure.*")
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddMeter("MyAdventure.*")
                .AddRuntimeInstrumentation()
                .AddConsoleExporter());

        return services;
    }

    public static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static string GetDefaultDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyAdventure");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "myadventure.db");
    }
}
ENDOFFILE

# =============================================================================
# 2. Desktop App.axaml.cs — register ToastService here
# =============================================================================

cat > src/MyAdventure.Desktop/App.axaml.cs << 'ENDOFFILE'
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
ENDOFFILE

# =============================================================================
# 3. Android App.axaml.cs — register ToastService here
# =============================================================================

cat > src/MyAdventure.Android/App.axaml.cs << 'ENDOFFILE'
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
ENDOFFILE

# =============================================================================
# 4. Move ToastServiceTests from Core.Tests to UI.Tests
#    UI.Tests already references Shared
# =============================================================================

# Remove from Core.Tests
rm -f tests/MyAdventure.Core.Tests/ToastServiceTests.cs

# Add to UI.Tests
cat > tests/MyAdventure.UI.Tests/ToastServiceTests.cs << 'ENDOFFILE'
using MyAdventure.Shared.Services;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class ToastServiceTests
{
    [Fact]
    public void Show_ShouldAddToast()
    {
        var service = new ToastService();
        service.Show("Test message");
        service.ActiveToasts.Count.ShouldBe(1);
        service.ActiveToasts[0].Message.ShouldBe("Test message");
    }

    [Fact]
    public void Show_MultipleTimes_ShouldAddMultiple()
    {
        var service = new ToastService();
        service.Show("One");
        service.Show("Two");
        service.Show("Three");
        service.ActiveToasts.Count.ShouldBe(3);
    }

    [Fact]
    public void CleanupExpired_ShouldRemoveExpiredToasts()
    {
        var service = new ToastService();
        service.Show("Will expire", TimeSpan.Zero);

        Thread.Sleep(10);

        service.CleanupExpired();
        service.ActiveToasts.Count.ShouldBe(0);
    }

    [Fact]
    public void CleanupExpired_ShouldKeepActiveToasts()
    {
        var service = new ToastService();
        service.Show("Still active", TimeSpan.FromMinutes(5));

        service.CleanupExpired();
        service.ActiveToasts.Count.ShouldBe(1);
    }

    [Fact]
    public void CleanupExpired_MixedToasts_ShouldOnlyRemoveExpired()
    {
        var service = new ToastService();
        service.Show("Expired", TimeSpan.Zero);
        service.Show("Active", TimeSpan.FromMinutes(5));

        Thread.Sleep(10);
        service.CleanupExpired();

        service.ActiveToasts.Count.ShouldBe(1);
        service.ActiveToasts[0].Message.ShouldBe("Active");
    }
}
ENDOFFILE

echo ""
echo "=== Fix applied ==="
echo ""
echo "Changes:"
echo "  1. Infrastructure/DependencyInjection.cs — removed ToastService registration (no Shared reference needed)"
echo "  2. Desktop/App.axaml.cs — registers ToastService singleton in DI"
echo "  3. Android/App.axaml.cs — registers ToastService singleton in DI"
echo "  4. Moved ToastServiceTests.cs from Core.Tests → UI.Tests"
echo ""
echo "Next: dotnet build && dotnet test"
