using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyAdventure.Infrastructure.Telemetry;

public static class TelemetryBootstrapper
{
    public static readonly ActivitySource Source = new("MyAdventure");

    public static IServiceCollection AddMyAdventureTelemetry(
        this IServiceCollection services,
        string environment,
        bool verbose,
        string sentryDsnStr)
    {
        var sentryDsn = SentryDsn.Parse(sentryDsnStr);

        // Define shared resource attributes
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: "MyAdventure",
                serviceVersion: typeof(TelemetryBootstrapper).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", environment)
            });

        // 1. Configure Logging
        services.AddLogging(logging =>
        {
            logging.ClearProviders();

            if (verbose)
            {
                logging.AddConsole(opt =>
                {
                    opt.LogToStandardErrorThreshold = LogLevel.Error;
                });
            }

            if (sentryDsn.IsOtlp)
            {
                Console.WriteLine($"Telemetry: Sentry OTLP enabled, env={environment}, verbose={verbose}");
                logging.AddOpenTelemetry(options =>
                {
                    options.SetResourceBuilder(resourceBuilder);
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    
                    options.AddOtlpExporter(opt =>
                    {
                        opt.Protocol = OtpExportProtocol.HttpProtobuf;
                        opt.Endpoint = new Uri(sentryDsn.OtlpLogsEndpoint);
                        opt.Headers = sentryDsn.OtlpHeaders;
                    });
                });
            }
        });

        // 2. Configure Tracing
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.AddSource(Source.Name);
                tracing.AddHttpClientInstrumentation();
                tracing.AddSqlStatements();

                if (sentryDsn.IsOtlp)
                {
                    tracing.AddOtlpExporter(opt =>
                    {
                        opt.Protocol = OtpExportProtocol.HttpProtobuf;
                        opt.Endpoint = new Uri(sentryDsn.OtlpTracesEndpoint);
                        opt.Headers = sentryDsn.OtlpHeaders;
                    });
                }
            });

        return services;
    }

    private static TracerProviderBuilder AddSqlStatements(this TracerProviderBuilder builder)
    {
        // Placeholder for any specific SQL tracking customization if needed
        return builder;
    }
}
