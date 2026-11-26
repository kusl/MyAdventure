using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using MyAdventure.Infrastructure.Telemetry;

namespace MyAdventure.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInfrastructure(string? dbPath = null) =>
            services.AddInfrastructure(new TelemetryOptions(), dbPath);

        public IServiceCollection AddInfrastructure(TelemetryOptions telemetry, string? dbPath = null)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath ?? DefaultDatabasePath() }.ToString();
            services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<DatabaseInitializer>();
            services.AddSingleton<IGameStateRepository, GameStateRepository>();
            services.AddSingleton<ISettingsRepository, SettingsRepository>();
            return services.AddTelemetry(telemetry);
        }
    }

    public static string DefaultDatabasePath()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyAdventure");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "myadventure.db");
    }
}
