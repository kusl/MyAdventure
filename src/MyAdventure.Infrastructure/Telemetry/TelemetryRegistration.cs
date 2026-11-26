using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure.Telemetry;

internal static class TelemetryRegistration
{
    private const string SourcePattern = "MyAdventure.*";

    public static IServiceCollection AddTelemetry(this IServiceCollection services, TelemetryOptions telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        var serviceVersion = ServiceVersion();
        var resource = ResourceBuilder.CreateDefault()
            .AddService(TelemetryDefaults.ServiceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object> { ["deployment.environment"] = telemetry.Sentry.Environment });

        string? parseError = null;
        var sentry = !string.IsNullOrWhiteSpace(telemetry.Sentry.Dsn) && SentryDsn.TryParse(telemetry.Sentry.Dsn, out var parsed, out parseError)
            ? parsed
            : null;

        services.AddSingleton(telemetry);
        services.AddSingleton(new TelemetryStatus(sentry is not null, parseError, telemetry.VerboseLogging, telemetry.Sentry.Environment));

        services.AddLogging(logging =>
        {
            logging.AddFilter("Microsoft.EntityFrameworkCore", telemetry.VerboseLogging ? LogLevel.Information : LogLevel.Warning);
            logging.SetMinimumLevel(telemetry.VerboseLogging ? LogLevel.Debug : LogLevel.Information);
            logging.AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(resource);
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;
                otel.ParseStateValues = true;
                otel.AddConsoleExporter();
                if (sentry is not null) otel.AddOtlpExporter(o => ConfigureOtlp(o, sentry.LogsEndpoint, sentry));
            });

            if (sentry is not null)
            {
                logging.AddProvider(new SentryEventLoggerProvider(sentry, TelemetryDefaults.ServiceName, serviceVersion, telemetry.Sentry.Environment));
            }
        });

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resource);
                tracing.AddSource(SourcePattern);
                tracing.SetSampler(new TraceIdRatioBasedSampler(Math.Clamp(telemetry.Sentry.TracesSampleRate, 0.0, 1.0)));
                tracing.AddConsoleExporter();
                if (sentry is not null) tracing.AddOtlpExporter(o => ConfigureOtlp(o, sentry.TracesEndpoint, sentry));
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resource);
                metrics.AddMeter(SourcePattern);
                metrics.AddRuntimeInstrumentation();
                metrics.AddConsoleExporter();
            });

        return services;
    }

    private static void ConfigureOtlp(OtlpExporterOptions options, string endpoint, SentryDsn sentry)
    {
        options.Endpoint = new Uri(endpoint);
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.Headers = $"x-sentry-auth={sentry.AuthHeaderValue}";
    }

    private static string ServiceVersion()
    {
        var assembly = typeof(TelemetryRegistration).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } version
            ? version
            : assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}

internal sealed record TelemetryStatus(bool SentryEnabled, string? SentryParseError, bool VerboseLogging, string Environment);
