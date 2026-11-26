namespace MyAdventure.Infrastructure.Telemetry;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public bool VerboseLogging { get; set; }

    public SentryOptions Sentry { get; set; } = new();
}

public sealed class SentryOptions
{
    public string? Dsn { get; set; }

    public string Environment { get; set; } = TelemetryDefaults.DefaultEnvironment;

    public double TracesSampleRate { get; set; } = 1.0;
}
