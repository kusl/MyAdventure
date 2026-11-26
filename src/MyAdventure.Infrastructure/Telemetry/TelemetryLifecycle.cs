using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure.Telemetry;

public enum TelemetryFlushMode
{
    Soft,
    Final,
}

public static class TelemetryLifecycle
{
    public static void Start(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.GetService<TracerProvider>();
        _ = services.GetService<MeterProvider>();

        var status = services.GetRequiredService<TelemetryStatus>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MyAdventure.Telemetry");

        if (status.SentryEnabled)
        {
            logger.LogInformation("Telemetry: Sentry enabled (OTLP logs/traces + envelope issues), env={Environment}, verbose={Verbose}", status.Environment, status.VerboseLogging);
        }
        else if (!string.IsNullOrEmpty(status.SentryParseError))
        {
            logger.LogWarning("Telemetry: Sentry DSN was provided but could not be parsed ({Error}); console-only.", status.SentryParseError);
        }
        else
        {
            logger.LogInformation("Telemetry: Sentry disabled (no DSN), verbose={Verbose}", status.VerboseLogging);
        }
    }

    public static Task FlushAsync(IServiceProvider services, TelemetryFlushMode mode = TelemetryFlushMode.Soft, int timeoutMilliseconds = 2000)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Task.Run(() =>
        {
            try
            {
                services.GetService<TracerProvider>()?.ForceFlush(timeoutMilliseconds);
                if (mode == TelemetryFlushMode.Final && services is IDisposable disposable) disposable.Dispose();
            }
            catch
            {
            }
        });
    }
}
