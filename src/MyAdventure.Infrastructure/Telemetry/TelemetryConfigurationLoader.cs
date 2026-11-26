using Microsoft.Extensions.Configuration;

namespace MyAdventure.Infrastructure.Telemetry;

public static class TelemetryConfigurationLoader
{
    public const string SentryDsnEnvVar = "SENTRY_DSN";
    public const string VerboseLoggingEnvVar = "MYADVENTURE_VERBOSE";
    public const string SentryEnvironmentEnvVar = "MYADVENTURE_SENTRY_ENVIRONMENT";

    public static TelemetryOptions Load(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        var builder = new ConfigurationBuilder();
        if (Path.IsPathRooted(directory) && Directory.Exists(directory))
        {
            builder.SetBasePath(directory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        }

        return LoadFromConfiguration(builder.AddEnvironmentVariables().Build());
    }

    public static TelemetryOptions LoadFromConfiguration(IConfiguration configuration) =>
        LoadFromConfiguration(configuration, TelemetryDefaults.DefaultDsn);

    internal static TelemetryOptions LoadFromConfiguration(IConfiguration configuration, string fallbackDsn)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Sentry.Dsn) && !string.IsNullOrWhiteSpace(fallbackDsn))
        {
            options.Sentry.Dsn = fallbackDsn;
        }

        ApplyEnvironmentOverrides(options);
        return options;
    }

    private static void ApplyEnvironmentOverrides(TelemetryOptions options)
    {
        if (Environment.GetEnvironmentVariable(SentryDsnEnvVar) is { } dsn && !string.IsNullOrWhiteSpace(dsn))
        {
            options.Sentry.Dsn = dsn;
        }

        if (Environment.GetEnvironmentVariable(VerboseLoggingEnvVar) is { } verbose && !string.IsNullOrWhiteSpace(verbose))
        {
            options.VerboseLogging = ParseBool(verbose);
        }

        if (Environment.GetEnvironmentVariable(SentryEnvironmentEnvVar) is { } environment && !string.IsNullOrWhiteSpace(environment))
        {
            options.Sentry.Environment = environment;
        }
    }

    private static bool ParseBool(string raw) => raw.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
}
