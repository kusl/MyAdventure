namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// Strongly-typed configuration for the OpenTelemetry pipeline.
/// Bound from the <c>Telemetry</c> section in <c>appsettings.json</c> and
/// from environment variables (see <see cref="TelemetryConfigurationLoader"/>
/// for the exact precedence rules).
///
/// <para>
/// Everything here has a safe, off-by-default value: a fresh checkout of
/// the project, a developer who has never heard of Sentry, and the CI
/// test runner all get the same behaviour — console-only OpenTelemetry,
/// no outbound network calls, no extra startup cost.
/// </para>
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// Configuration section name (<c>appsettings.json</c>).
    /// </summary>
    public const string SectionName = "Telemetry";

    /// <summary>
    /// When true, the OpenTelemetry logger's minimum level is dropped to
    /// <c>Debug</c> (and the EF Core category lifts from <c>Warning</c>
    /// to <c>Information</c>). Useful for chasing bugs without forcing
    /// every release build to emit a tick-by-tick log stream. Default
    /// is <c>false</c>.
    ///
    /// <para>
    /// Toggle via <c>Telemetry:VerboseLogging</c> in <c>appsettings.json</c>
    /// or the <c>MYADVENTURE_VERBOSE</c> environment variable (any value
    /// other than <c>0</c>/<c>false</c> enables it).
    /// </para>
    /// </summary>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Sentry-specific options. Honoured only when
    /// <see cref="SentryOptions.Dsn"/> is non-empty.
    /// </summary>
    public SentryOptions Sentry { get; set; } = new();
}

/// <summary>
/// Settings for forwarding logs and traces to Sentry via OTLP/HTTP.
///
/// <para>
/// This project deliberately does <b>not</b> use the Sentry .NET SDK.
/// Sentry's hosted ingestion accepts the standard OpenTelemetry
/// Protocol natively (traces + logs; metrics are not supported by
/// Sentry over OTLP), so the existing OpenTelemetry stack can talk
/// to it directly with no vendor-specific NuGet package. Swapping to
/// any other OTLP backend (Grafana Cloud, Honeycomb, Tempo, Loki, an
/// OpenTelemetry Collector, etc.) becomes a one-line config change.
/// </para>
/// </summary>
public sealed class SentryOptions
{
    /// <summary>
    /// The Sentry DSN. When empty or <c>null</c>, the Sentry OTLP
    /// exporters are not registered and the app behaves exactly as it
    /// did before the Sentry integration existed.
    ///
    /// <para>
    /// Looks like <c>https://&lt;key&gt;@o&lt;org&gt;.ingest.&lt;region&gt;.sentry.io/&lt;project&gt;</c>.
    /// The DSN encodes the public key, org id, ingest region, and
    /// project id; <see cref="SentryDsn.Parse"/> derives the OTLP
    /// endpoint URLs and auth header from it.
    /// </para>
    ///
    /// <para>
    /// Honour order (highest precedence first):
    /// <list type="number">
    ///   <item>The <c>SENTRY_DSN</c> environment variable.</item>
    ///   <item>The <c>Telemetry:Sentry:Dsn</c> key in
    ///   <c>appsettings.json</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public string? Dsn { get; set; }

    /// <summary>
    /// Tag every event with the deployment environment. Common values
    /// are <c>production</c>, <c>staging</c>, <c>development</c>. Default
    /// is <c>production</c> so that the rare developer who turns Sentry
    /// on locally can override it explicitly and avoid polluting the
    /// production stream with dev noise.
    /// </summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Fraction of traces to sample [0.0 - 1.0]. Default is 1.0 (sample
    /// everything) because the game emits very few spans per session
    /// — there's no risk of blowing the free quota at full sampling.
    /// Lower it if you start running large automated soak tests.
    /// </summary>
    public double TracesSampleRate { get; set; } = 1.0;
}
