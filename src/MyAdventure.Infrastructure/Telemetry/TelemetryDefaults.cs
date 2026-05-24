namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// Compile-time fallback values for the OpenTelemetry pipeline. Used by
/// <see cref="TelemetryConfigurationLoader"/> when neither
/// <c>appsettings.json</c> nor environment variables supply a value.
///
/// <para>
/// <b>Why a hardcoded DSN lives in source.</b> During the project's
/// testing phase we want a fresh clone — and binaries pulled from
/// GitHub Releases — to start reporting to Sentry immediately, without
/// the player or developer first having to set <c>SENTRY_DSN</c> or
/// edit any config file. The DSN below is a low-privilege public key
/// that only grants permission to <i>write</i> events to a specific
/// Sentry project; it is not a secret in the cryptographic sense. (For
/// the same reason mobile and frontend Sentry SDKs ship DSNs baked into
/// the published binary.) Once we move past the testing phase this
/// constant can be set back to <c>""</c> and Sentry returns to being
/// opt-in.
/// </para>
///
/// <para>
/// <b>One DSN, all platforms.</b> A Sentry DSN identifies a Sentry
/// <i>project</i>, not a client platform. The same DSN is used by the
/// Desktop app on Windows/Linux/macOS and by the Android app — Sentry
/// distinguishes them by the <c>service.name</c>, <c>service.version</c>,
/// and <c>deployment.environment</c> resource attributes that the
/// OpenTelemetry pipeline already attaches to every event. If you ever
/// want to split Desktop and Android into separate Sentry projects, the
/// honest way to do it is to create a new Sentry project, take its DSN,
/// and override <see cref="DefaultDsn"/> per-platform via the existing
/// <c>SENTRY_DSN</c> environment variable — no code change required.
/// </para>
///
/// <para>
/// <b>To rotate or revoke this DSN:</b> in Sentry go to
/// <c>Settings → Projects → &lt;project&gt; → Client Keys (DSN)</c>,
/// disable the old key, and create a new one. Update the value below
/// and ship a new build.
/// </para>
/// </summary>
internal static class TelemetryDefaults
{
    /// <summary>
    /// Hardcoded fallback DSN. Overridable by <c>appsettings.json</c>
    /// (Desktop) or the <c>SENTRY_DSN</c> environment variable (both
    /// platforms). Set to <c>""</c> to disable the compile-time
    /// fallback and require explicit configuration.
    /// </summary>
    public const string DefaultDsn =
        "https://fe6ae5ee15285c313b8171bb7a5a4ad0@o4511444968079360.ingest.de.sentry.io/4511444969390160";

    /// <summary>
    /// Default deployment environment when nothing else is specified.
    /// </summary>
    public const string DefaultEnvironment = "production";
}
