using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Core.Services;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? dbPath = null)
    {
        var path = dbPath ?? GetDefaultDbPath();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={path}"));

        services.AddScoped<IGameStateRepository, GameStateRepository>();
        services.AddScoped<GameEngine>();

        // Console logging only works on desktop; Android has no System.Console.
        // On Android, logs go through Android.Util.Log instead.
        var isAndroid = OperatingSystem.IsAndroid();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            if (!isAndroid)
            {
                builder.AddConsole();
            }
        });

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource("MyAdventure.*");
                if (!isAndroid)
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("MyAdventure.*");
                // RuntimeInstrumentation may not be supported on all platforms
                if (!isAndroid)
                {
                    metrics.AddRuntimeInstrumentation();
                    metrics.AddConsoleExporter();
                }
            });

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
        string appData;

        if (OperatingSystem.IsAndroid())
        {
            // On Android, use the app's internal files directory.
            // Environment.GetFolderPath may return empty or unexpected paths.
            appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (string.IsNullOrEmpty(appData))
            {
                // Fallback: use the app's base directory
                appData = AppContext.BaseDirectory;
            }
        }
        else
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyAdventure");
        }

        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "myadventure.db");
    }
}
