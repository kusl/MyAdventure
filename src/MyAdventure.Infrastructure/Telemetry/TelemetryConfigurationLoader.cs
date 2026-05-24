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
///   <item>The compile-time fallback in <see cref="TelemetryDefaults"/>.
///   During the testing phase the DSN there is non-empty so a freshly
///   built or freshly downloaded binary reports to Sentry without any
///   configuration on the user's machine.</item>
///   <item>Plain defaults from <see cref="TelemetryOptions"/>'s property
///   initializers — Sentry off, verbose off — which apply when the
///   compile-time fallback is empty.</item>
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
        => LoadFromEnvironment(TelemetryDefaults.DefaultDsn);

    /// <summary>
    /// Build options by binding <c>Telemetry</c> in
    /// <paramref name="configuration"/>, layering environment overrides
    /// on top, and using the compile-time fallback as the floor (Desktop
    /// path).
    /// </summary>
    public static TelemetryOptions LoadFromConfiguration(IConfiguration configuration)
        => LoadFromConfiguration(configuration, TelemetryDefaults.DefaultDsn);

    /// <summary>
    /// Test-friendly overload that lets the caller substitute a different
    /// compile-time DSN fallback. Production code uses the public
    /// parameterless variant; the unit tests use this one to exercise both
    /// "fallback present" and "fallback empty" paths without rebuilding
    /// the assembly.
    /// </summary>
    internal static TelemetryOptions LoadFromEnvironment(string fallbackDsn)
    {
        var options = new TelemetryOptions();
        ApplyCompileTimeFallback(options, fallbackDsn);
        ApplyEnvironmentOverrides(options);
        return options;
    }

    /// <summary>
    /// Test-friendly overload — see
    /// <see cref="LoadFromEnvironment(string)"/>.
    /// </summary>
    internal static TelemetryOptions LoadFromConfiguration(
        IConfiguration configuration, string fallbackDsn)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new TelemetryOptions();
        ApplyCompileTimeFallback(options, fallbackDsn);
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);

        // Bind() will overwrite our fallback DSN with whatever is in the
        // config — including the empty string, which is the literal value
        // we ship in appsettings.json for the "no DSN configured" case.
        // We want the compile-time fallback to win over "Dsn=''" but lose
        // to "Dsn='https://...'". The cleanest way to express that is:
        // if the bound DSN is empty after Bind, restore the fallback.
        if (string.IsNullOrWhiteSpace(options.Sentry.Dsn))
        {
            options.Sentry.Dsn = fallbackDsn;
        }

        ApplyEnvironmentOverrides(options);
        return options;
    }

    /// <summary>
    /// Seed the options with the supplied <paramref name="fallbackDsn"/>.
    /// Higher-precedence sources are expected to overwrite these afterwards.
    /// </summary>
    private static void ApplyCompileTimeFallback(TelemetryOptions options, string fallbackDsn)
    {
        if (!string.IsNullOrWhiteSpace(fallbackDsn))
        {
            options.Sentry.Dsn = fallbackDsn;
        }
        options.Sentry.Environment = TelemetryDefaults.DefaultEnvironment;
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
