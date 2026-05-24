namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// A startup-time snapshot of the decisions
/// <see cref="DependencyInjection.AddInfrastructure(Microsoft.Extensions.DependencyInjection.IServiceCollection, TelemetryOptions, string?)"/>
/// made while wiring the OpenTelemetry pipeline. Stored as a singleton
/// service so the entry point can emit a single human-readable startup
/// log line through the very pipeline that was just built — see
/// <see cref="DependencyInjection.EmitStartupBreadcrumb"/>.
///
/// <para>
/// Kept <c>internal</c> deliberately: outside the Infrastructure project
/// nobody should be reading these flags individually. The startup
/// breadcrumb is the one supported surface.
/// </para>
/// </summary>
internal sealed record TelemetryStartupAnnouncement(
    bool SentryEnabled,
    string? SentryParseError,
    bool VerboseLogging,
    string Environment);
