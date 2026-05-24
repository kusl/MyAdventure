using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Repositories;
using MyAdventure.Infrastructure.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Convenience overload preserved for callers (and tests) that don't
    /// want to opt into the telemetry configuration object. Behaviour is
    /// unchanged from before: console exporters only, no Sentry, info-level
    /// logging — exactly what every existing test relies on.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string? dbPath = null)
        => AddInfrastructure(services, new TelemetryOptions(), dbPath);

    /// <summary>
    /// Register everything the Infrastructure layer owns: the SQLite
    /// <see cref="AppDbContext"/>, the <see cref="IGameStateRepository"/>,
    /// and the full OpenTelemetry logging/tracing/metrics pipeline.
    ///
    /// <para>
    /// <b>Sentry integration.</b> If <see cref="SentryOptions.Dsn"/> is
    /// populated <i>and</i> parses successfully, an OTLP/HTTP exporter
    /// is registered for both logs and traces (Sentry doesn't accept
    /// OTLP metrics, so the metrics pipeline stays console-only). The
    /// DSN, environment, and sampling rate come from
    /// <paramref name="telemetry"/>; callers usually build that via
    /// <see cref="TelemetryConfigurationLoader.LoadFromConfiguration"/>
    /// (Desktop) or <see cref="TelemetryConfigurationLoader.LoadFromEnvironment"/>
    /// (Android).
    /// </para>
    ///
    /// <para>
    /// <b>Verbose logging.</b> When <see cref="TelemetryOptions.VerboseLogging"/>
    /// is true the OpenTelemetry log pipeline's minimum level drops to
    /// <c>Debug</c> and Entity Framework Core's category lifts from
    /// <c>Warning</c> to <c>Information</c>, so SQL command traces start
    /// showing up. This is a runtime switch — no rebuild needed; the
    /// player (or a beta tester) can toggle it via
    /// <c>MYADVENTURE_VERBOSE=1</c> or by editing
    /// <c>appsettings.json</c>.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        TelemetryOptions telemetry,
        string? dbPath = null)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        dbPath ??= GetDefaultDbPath();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<IGameStateRepository, GameStateRepository>();

        // Make the snapshotted options available to anyone who wants to
        // inspect them at runtime (the App startup logs them).
        services.AddSingleton(telemetry);

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: "MyAdventure",
                serviceVersion: GetAssemblyVersion(),
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", telemetry.Sentry.Environment),
            });

        // Parse the DSN exactly once. If it's invalid we proceed with the
        // console exporter only — a misconfigured DSN must never block
        // app startup. The parse error is captured in a logger message
        // emitted by the very pipeline we're building, so it shows up on
        // the same console the developer is already watching.
        SentryDsn? sentry = null;
        string? sentryParseError = null;
        var sentryEnabled = !string.IsNullOrWhiteSpace(telemetry.Sentry.Dsn)
            && SentryDsn.TryParse(telemetry.Sentry.Dsn, out sentry, out sentryParseError);

        ConfigureLogging(services, telemetry, resourceBuilder, sentry);
        ConfigureTracingAndMetrics(services, telemetry, resourceBuilder, sentry);

        // Emit a single-line breadcrumb that records the configuration
        // we landed on. We can't log it directly here (no IServiceProvider
        // yet), so we use a transient hosted-style activator: register a
        // startup-time announcer that the app calls explicitly via
        // EmitStartupBreadcrumb.
        services.AddSingleton(new TelemetryStartupAnnouncement(
            SentryEnabled: sentryEnabled,
            SentryParseError: sentryParseError,
            VerboseLogging: telemetry.VerboseLogging,
            Environment: telemetry.Sentry.Environment));

        return services;
    }

    private static void ConfigureLogging(
        IServiceCollection services,
        TelemetryOptions telemetry,
        ResourceBuilder resourceBuilder,
        SentryDsn? sentry)
    {
        services.AddLogging(logging =>
        {
            // Lift EF Core noise to Information when verbose mode is on,
            // otherwise keep it pinned at Warning so the default
            // OpenTelemetry log pipeline doesn't spam Sentry with
            // benign EnsureCreated chatter.
            logging.AddFilter("Microsoft.EntityFrameworkCore",
                telemetry.VerboseLogging ? LogLevel.Information : LogLevel.Warning);

            logging.SetMinimumLevel(telemetry.VerboseLogging ? LogLevel.Debug : LogLevel.Information);

            logging.AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(resourceBuilder);
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;
                otel.ParseStateValues = true;

                otel.AddConsoleExporter();

                if (sentry is not null)
                {
                    otel.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(sentry.LogsEndpoint);
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                        o.Headers = $"x-sentry-auth={sentry.AuthHeaderValue}";
                    });
                }
            });
        });
    }

    private static void ConfigureTracingAndMetrics(
        IServiceCollection services,
        TelemetryOptions telemetry,
        ResourceBuilder resourceBuilder,
        SentryDsn? sentry)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.AddSource("MyAdventure.*");
                tracing.SetSampler(new TraceIdRatioBasedSampler(
                    Math.Clamp(telemetry.Sentry.TracesSampleRate, 0.0, 1.0)));

                tracing.AddConsoleExporter();

                if (sentry is not null)
                {
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(sentry.TracesEndpoint);
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                        o.Headers = sentry.AuthHeaderValue;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                // Sentry's OTLP ingestion does NOT accept metrics, so the
                // metrics pipeline stays console-only. The runtime metrics
                // are still useful locally and would be picked up by any
                // separate OTLP backend (Grafana Mimir, Prometheus via
                // OTLP, etc.) when added later.
                metrics.SetResourceBuilder(resourceBuilder);
                metrics.AddMeter("MyAdventure.*");
                metrics.AddRuntimeInstrumentation();
                metrics.AddConsoleExporter();
            });
    }

    /// <summary>
    /// Emit a single startup log line summarising the telemetry decisions
    /// we made in <see cref="AddInfrastructure(IServiceCollection, TelemetryOptions, string?)"/>.
    /// The Desktop and Android entry points call this once after building
    /// the service provider, so the configuration shows up in every log
    /// sink (console, OTLP/Sentry, Android logcat) without the
    /// Infrastructure project needing to know about any of them directly.
    /// </summary>
    public static void EmitStartupBreadcrumb(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var announcement = services.GetRequiredService<TelemetryStartupAnnouncement>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("MyAdventure.Telemetry");

        if (announcement.SentryEnabled)
        {
            logger.LogInformation(
                "Telemetry: Sentry OTLP enabled, env={Environment}, verbose={Verbose}",
                announcement.Environment, announcement.VerboseLogging);
        }
        else if (!string.IsNullOrEmpty(announcement.SentryParseError))
        {
            logger.LogWarning(
                "Telemetry: Sentry DSN was provided but could not be parsed ({Error}); console-only.",
                announcement.SentryParseError);
        }
        else
        {
            logger.LogInformation(
                "Telemetry: Sentry disabled (no DSN), verbose={Verbose}",
                announcement.VerboseLogging);
        }
    }

    /// <summary>
    /// Initialize the SQLite database, applying an in-place schema migration
    /// to lift legacy REAL columns into the new TEXT (BigDouble) columns when
    /// an old v1 database is detected.
    /// <para>
    /// The BigDouble migration renamed the three numeric columns
    /// (<c>Cash</c>, <c>LifetimeEarnings</c>, <c>AngelInvestors</c>) to their
    /// <c>*Text</c> counterparts. We migrate in-place rather than dropping
    /// the table so any existing player progress is preserved across the
    /// schema change without requiring an export/import. The migration runs
    /// in a single transaction so a crash mid-migration leaves the old
    /// schema intact (no half-migrated database).
    /// </para>
    /// </summary>
    public static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DbInit");

        // Run the schema migration BEFORE EnsureCreated. EnsureCreated is a
        // no-op when the tables already exist, so it won't fix an old schema
        // for us — we have to do that explicitly.
        await MigrateLegacySchemaIfNeededAsync(db, logger);

        // Create the database / any genuinely missing tables idempotently.
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Inspect the GameStates table; if it has the legacy REAL columns,
    /// translate them to the new TEXT columns and drop the old ones.
    /// Idempotent — a fresh database or an already-migrated database
    /// passes straight through.
    /// </summary>
    private static async Task MigrateLegacySchemaIfNeededAsync(AppDbContext db, ILogger? logger)
    {
        var conn = (SqliteConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        // If the GameStates table doesn't exist at all yet, EnsureCreated
        // will make it with the new schema — nothing to migrate.
        var hasTable = await TableExistsAsync(conn, "GameStates");
        if (!hasTable) return;

        var columns = await GetColumnNamesAsync(conn, "GameStates");

        // Already migrated (or fresh-with-new-schema): the new columns exist.
        if (columns.Contains("CashText")) return;

        // No old columns either: nothing to do.
        if (!columns.Contains("Cash")) return;

        logger?.LogInformation("Migrating GameStates table to BigDouble TEXT schema");

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(conn, tx,
                "ALTER TABLE GameStates ADD COLUMN CashText TEXT NOT NULL DEFAULT '0'");
            await ExecuteAsync(conn, tx,
                "ALTER TABLE GameStates ADD COLUMN LifetimeEarningsText TEXT NOT NULL DEFAULT '0'");
            await ExecuteAsync(conn, tx,
                "ALTER TABLE GameStates ADD COLUMN AngelInvestorsText TEXT NOT NULL DEFAULT '0'");

            // SQLite's CAST(double AS TEXT) produces an InvariantCulture
            // string representation that BigDouble.Parse will happily
            // round-trip (it falls back to plain double.Parse for any
            // numeric string that doesn't look like the canonical form).
            await ExecuteAsync(conn, tx,
                "UPDATE GameStates SET " +
                "CashText = CAST(Cash AS TEXT), " +
                "LifetimeEarningsText = CAST(LifetimeEarnings AS TEXT), " +
                "AngelInvestorsText = CAST(AngelInvestors AS TEXT)");

            // SQLite 3.35+ (EF Core 10 ships with a much newer version)
            // supports ALTER TABLE DROP COLUMN, so we don't need the
            // historical table-rebuild dance.
            await ExecuteAsync(conn, tx, "ALTER TABLE GameStates DROP COLUMN Cash");
            await ExecuteAsync(conn, tx, "ALTER TABLE GameStates DROP COLUMN LifetimeEarnings");
            await ExecuteAsync(conn, tx, "ALTER TABLE GameStates DROP COLUMN AngelInvestors");

            await tx.CommitAsync();
            logger?.LogInformation("BigDouble migration complete");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger?.LogError(ex, "BigDouble migration failed; legacy schema retained");
            throw;
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name";
        cmd.Parameters.AddWithValue("$name", table);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqliteConnection conn, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        // PRAGMA table_info doesn't accept parameter binding for the table
        // name; the value comes from a trusted constant here, not user input.
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Column index 1 is the column name in PRAGMA table_info's output.
            result.Add(reader.GetString(1));
        }
        return result;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetDefaultDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyAdventure");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "myadventure.db");
    }

    /// <summary>
    /// Read the assembly's InformationalVersion at runtime (set by
    /// <c>Directory.Build.props</c>) so the OpenTelemetry resource is
    /// tagged with the build number. Falls back to "0.0.0" if the
    /// attribute is missing — which would only happen in an oddly
    /// stripped build, so we don't make it fatal.
    /// </summary>
    private static string GetAssemblyVersion()
    {
        var asm = typeof(DependencyInjection).Assembly;
        var info = asm.GetCustomAttributes(
            typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
        if (info.Length > 0 &&
            info[0] is System.Reflection.AssemblyInformationalVersionAttribute attr &&
            !string.IsNullOrWhiteSpace(attr.InformationalVersion))
        {
            return attr.InformationalVersion;
        }
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}
