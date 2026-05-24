using Microsoft.Extensions.Configuration;

namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// Builds a <see cref="TelemetryOptions"/> from environment variables and
/// (optionally) an <see cref="IConfiguration"/>. Lives in
/// <c>Infrastructure</c> so both Desktop and Android can share it.
///
/// <para>
/// <b>Why this exists.</b> Android does not ship with the typical .NET
/// host bootstrapping that auto-binds <c>appsettings.json</c>; the
/// Android project's <c>App.axaml.cs</c> calls <see cref="LoadFromEnvironment"/>
/// directly, while the Desktop project loads <c>appsettings.json</c> first
/// and then merges environment overrides via <see cref="LoadFromConfiguration"/>.
/// Both code paths end up with identical <see cref="TelemetryOptions"/>
/// semantics, which is what lets us keep a single
/// <see cref="DependencyInjection.AddInfrastructure(Microsoft.Extensions.DependencyInjection.IServiceCollection, TelemetryOptions, string?)"/>
/// overload servicing both platforms.
/// </para>
///
/// <para>
/// Honour order (highest precedence wins):
/// <list type="number">
///   <item>The <c>SENTRY_DSN</c> / <c>MYADVENTURE_VERBOSE</c> /
///   <c>MYADVENTURE_SENTRY_ENVIRONMENT</c> environment variables.</item>
///   <item>The bound <see cref="TelemetryOptions"/> values (which usually
///   come from <c>appsettings.json</c>).</item>
///   <item>Compile-time defaults (Sentry off, verbose off).</item>
/// </list>
/// </para>
/// </summary>
public static class TelemetryConfigurationLoader
{
    public const string SentryDsnEnvVar = "SENTRY_DSN";
    public const string VerboseLoggingEnvVar = "MYADVENTURE_VERBOSE";
    public const string SentryEnvironmentEnvVar = "MYADVENTURE_SENTRY_ENVIRONMENT";

    /// <summary>
    /// Build options from environment variables only (Android path).
    /// </summary>
    public static TelemetryOptions LoadFromEnvironment()
    {
        var options = new TelemetryOptions();
        ApplyEnvironmentOverrides(options);
        return options;
    }

    /// <summary>
    /// Build options by binding <c>Telemetry</c> in
    /// <paramref name="configuration"/> and then applying environment
    /// overrides on top (Desktop path).
    /// </summary>
    public static TelemetryOptions LoadFromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);
        ApplyEnvironmentOverrides(options);
        return options;
    }

    private static void ApplyEnvironmentOverrides(TelemetryOptions options)
    {
        var dsn = Environment.GetEnvironmentVariable(SentryDsnEnvVar);
        if (!string.IsNullOrWhiteSpace(dsn))
        {
            options.Sentry.Dsn = dsn;
        }

        var verbose = Environment.GetEnvironmentVariable(VerboseLoggingEnvVar);
        if (!string.IsNullOrWhiteSpace(verbose))
        {
            options.VerboseLogging = ParseBool(verbose);
        }

        var env = Environment.GetEnvironmentVariable(SentryEnvironmentEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            options.Sentry.Environment = env;
        }
    }

    /// <summary>
    /// Tolerant boolean parser: accepts <c>true</c>/<c>1</c>/<c>yes</c>/<c>on</c>
    /// (any casing) as true; treats anything else, including <c>0</c>/<c>false</c>,
    /// as false. People set env vars in strange ways.
    /// </summary>
    private static bool ParseBool(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("1", StringComparison.Ordinal)
            || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
