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

        // Toast service is a singleton shared across all VMs
        services.AddSingleton<MyAdventure.Shared.Services.ToastService>();

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
