using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace MyAdventure.Infrastructure.Telemetry;

internal sealed class SentryEventLoggerProvider : ILoggerProvider
{
    private const string EnvelopeContentType = "application/x-sentry-envelope";

    private readonly SentryDsn _dsn;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _environment;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SentryEventLogger> _loggers = new();
    private readonly CancellationTokenSource _cts = new();

    public SentryEventLoggerProvider(SentryDsn dsn, string serviceName, string serviceVersion, string environment)
        : this(dsn, new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, ownsHttpClient: true, serviceName, serviceVersion, environment, TimeProvider.System)
    {
    }

    internal SentryEventLoggerProvider(SentryDsn dsn, HttpClient http, string serviceName, string serviceVersion, string environment, TimeProvider timeProvider)
        : this(dsn, http, ownsHttpClient: false, serviceName, serviceVersion, environment, timeProvider)
    {
    }

    private SentryEventLoggerProvider(
        SentryDsn dsn,
        HttpClient http,
        bool ownsHttpClient,
        string serviceName,
        string serviceVersion,
        string environment,
        TimeProvider timeProvider)
    {
        _dsn = dsn ?? throw new ArgumentNullException(nameof(dsn));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _serviceVersion = serviceVersion ?? throw new ArgumentNullException(nameof(serviceVersion));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _ownsHttpClient = ownsHttpClient;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static (name, provider) => new SentryEventLogger(provider, name), this);

    public void Dispose()
    {
        _cts.Cancel();
        if (_ownsHttpClient) _http.Dispose();
        _cts.Dispose();
    }

    internal static bool ShouldPromoteToIssue(LogLevel level, Exception? exception) =>
        exception is not null || level == LogLevel.Critical;

    internal void Enqueue(SentryEventPayload payload)
    {
        byte[] bytes;
        try
        {
            bytes = SentryEnvelope.BuildExceptionEvent(payload, _dsn, _serviceName, _serviceVersion, _environment, _timeProvider.GetUtcNow());
        }
        catch
        {
            return;
        }

        _ = SendAsync(bytes, _cts.Token);
    }

    private async Task SendAsync(byte[] bytes, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _dsn.EnvelopeEndpoint)
            {
                Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue(EnvelopeContentType) } },
            };
            request.Headers.TryAddWithoutValidation("X-Sentry-Auth", _dsn.AuthHeaderValue);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}

internal sealed class SentryEventLogger(SentryEventLoggerProvider provider, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!SentryEventLoggerProvider.ShouldPromoteToIssue(logLevel, exception)) return;

        provider.Enqueue(new SentryEventPayload(
            Exception: exception ?? new SentryCriticalLogException(formatter(state, null)),
            Message: formatter(state, exception),
            Level: ToSentryLevel(logLevel),
            LoggerCategory: category));
    }

    private static string ToSentryLevel(LogLevel level) => level switch
    {
        LogLevel.Critical => "fatal",
        LogLevel.Error => "error",
        LogLevel.Warning => "warning",
        LogLevel.Information => "info",
        LogLevel.Debug or LogLevel.Trace => "debug",
        _ => "error",
    };
}

internal sealed class SentryCriticalLogException(string message) : Exception(message);
