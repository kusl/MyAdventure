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

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
        });

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
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
