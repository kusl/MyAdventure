using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// An <see cref="ILoggerProvider"/> that posts every log record carrying
/// an exception (or with <see cref="LogLevel.Error"/>/<see cref="LogLevel.Critical"/>
/// severity) to Sentry's classic envelope endpoint, so that the event
/// shows up as an <i>Issue</i> in the Sentry UI.
///
/// <para>
/// <b>Why this exists.</b> The project's existing OpenTelemetry log
/// pipeline already forwards every log record to Sentry's OTLP logs
/// endpoint — that populates the Sentry <i>Logs</i> panel but, per
/// Sentry's published OTLP documentation, it does <b>not</b> create
/// Issues. Sentry's OTLP traces endpoint also drops span exception
/// events during ingestion. The classic envelope endpoint, in
/// contrast, is exactly what every Sentry SDK ultimately POSTs to
/// when capturing an exception, and it is what populates the Issues
/// panel.
/// </para>
///
/// <para>
/// We deliberately do <b>not</b> use the Sentry .NET SDK NuGet
/// package. The project's FOSS-only NuGet policy excludes SDKs whose
/// licensing terms are vendor-specific, and the envelope wire format
/// is documented and stable. A ~120-line <see cref="HttpClient"/>
/// wrapper keeps us on standard, swappable building blocks (the same
/// pattern, pointed at any other backend, would Just Work).
/// </para>
///
/// <para>
/// <b>Fire-and-forget posting.</b> Each triggering log record is
/// serialized inline on the calling thread (cheap; the envelope
/// builder is allocation-light) and posted via a background
/// <see cref="Task"/>. We do not block the caller waiting for the
/// network — a slow Sentry response must never make a game tick stutter.
/// Failures are swallowed; the worst case is that an Issue does not
/// appear in Sentry, which is no worse than not having Sentry at all.
/// </para>
///
/// <para>
/// <b>Tests.</b> The HTTP transport is overridable via
/// <see cref="HttpMessageHandler"/> injection so tests can capture
/// the bytes that would have gone to the network and pin both the
/// envelope contents and the request shape without ever touching the
/// real Sentry endpoint.
/// </para>
/// </summary>
internal sealed class SentryEventLoggerProvider : ILoggerProvider
{
    private readonly SentryDsn _dsn;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _environment;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SentryEventLogger> _loggers = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Construct a provider that owns its own <see cref="HttpClient"/>
    /// — the typical case at runtime. The handler is configured with
    /// a modest connect timeout so a misconfigured DSN host doesn't
    /// hold pending logs in memory forever.
    /// </summary>
    public SentryEventLoggerProvider(
        SentryDsn dsn,
        string serviceName,
        string serviceVersion,
        string environment,
        TimeProvider? timeProvider = null)
        : this(
            dsn,
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            ownsHttpClient: true,
            serviceName: serviceName,
            serviceVersion: serviceVersion,
            environment: environment,
            timeProvider: timeProvider ?? TimeProvider.System)
    {
    }

    /// <summary>
    /// Test-friendly constructor that lets the caller hand in an
    /// <see cref="HttpClient"/> backed by a fake
    /// <see cref="HttpMessageHandler"/>. The provider does <i>not</i>
    /// dispose externally-supplied clients.
    /// </summary>
    internal SentryEventLoggerProvider(
        SentryDsn dsn,
        HttpClient http,
        string serviceName,
        string serviceVersion,
        string environment,
        TimeProvider timeProvider)
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
        ArgumentNullException.ThrowIfNull(dsn);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(serviceVersion);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dsn = dsn;
        _http = http;
        _ownsHttpClient = ownsHttpClient;
        _serviceName = serviceName;
        _serviceVersion = serviceVersion;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new SentryEventLogger(this, name));

    public void Dispose()
    {
        _cts.Cancel();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
        _cts.Dispose();
    }

    /// <summary>
    /// Decide whether a given log record should be promoted to a
    /// Sentry Issue. Today the rule is: any record carrying an
    /// <see cref="Exception"/>, regardless of severity, plus any
    /// record at <see cref="LogLevel.Critical"/>. Plain
    /// <see cref="LogLevel.Error"/> records without an exception go
    /// only to the Logs panel (via the OTLP logs exporter); promoting
    /// them to Issues would flood the project with low-signal events
    /// like EF Core's "First/FirstOrDefault without OrderBy" warning.
    /// </summary>
    internal static bool ShouldPromoteToIssue(LogLevel level, Exception? exception)
    {
        if (exception is not null)
        {
            return true;
        }
        return level == LogLevel.Critical;
    }

    /// <summary>
    /// Build the envelope bytes and post them on a background
    /// <see cref="Task"/>. Synchronous part runs on the calling thread
    /// because the only cost is JSON serialization; the network IO
    /// is decoupled. Exposed internally so the test logger can drive
    /// it directly with a synthetic payload.
    /// </summary>
    internal void Enqueue(SentryEventPayload payload)
    {
        byte[] bytes;
        try
        {
            bytes = SentryEnvelope.BuildExceptionEvent(
                payload,
                _dsn,
                _serviceName,
                _serviceVersion,
                _environment,
                _timeProvider.GetUtcNow());
        }
        catch
        {
            // Never let envelope-building exceptions escape into the
            // logging pipeline; that would create an infinite recursion
            // if the consumer logged this failure too.
            return;
        }

        // Fire-and-forget — discard the resulting Task. We do not want
        // backpressure from a slow Sentry to surface as game-tick lag.
        _ = SendAsync(bytes, _cts.Token);
    }

    private async Task SendAsync(byte[] bytes, CancellationToken ct)
    {
        try
        {
            using var content = new ByteArrayContent(bytes);
            // Sentry's envelope endpoint expects this content type; the
            // body is newline-delimited JSON, not a single JSON object,
            // hence "x-sentry-envelope" rather than "application/json".
            content.Headers.ContentType =
                new MediaTypeHeaderValue("application/x-sentry-envelope");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, _dsn.EnvelopeEndpoint)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation(
                "X-Sentry-Auth", _dsn.AuthHeaderValue);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            // We intentionally do nothing with the response. If Sentry
            // rejected the event (rate limit, malformed envelope, etc.)
            // there is no useful local recovery action and logging the
            // failure would risk recursing back into the same provider.
            _ = response.StatusCode;
        }
        catch
        {
            // Swallow. See class XML doc for rationale.
        }
    }
}

/// <summary>
/// <see cref="ILogger"/> implementation paired with
/// <see cref="SentryEventLoggerProvider"/>. One instance per category
/// name. Filtering by <see cref="LogLevel"/> happens entirely inside
/// <see cref="SentryEventLoggerProvider.ShouldPromoteToIssue"/> — this
/// logger always claims to be enabled so it has a chance to inspect
/// the exception attached to each record.
/// </summary>
internal sealed class SentryEventLogger : ILogger
{
    private readonly SentryEventLoggerProvider _provider;
    private readonly string _category;

    public SentryEventLogger(SentryEventLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>
    /// We can't make the routing decision here because
    /// <see cref="IsEnabled"/> doesn't get the exception. Claim enabled
    /// for the relevant range and re-check inside <see cref="Log"/>.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!SentryEventLoggerProvider.ShouldPromoteToIssue(logLevel, exception))
        {
            return;
        }

        // Synthetic exception fallback: if the caller logged at
        // Critical without attaching an exception, create a placeholder
        // so the Sentry Issue still has a usable type/value. Stack
        // trace will be the call site of the synthetic capture, not
        // perfect but better than discarding the signal entirely.
        var ex = exception ?? new SentryCriticalLogException(formatter(state, null));

        var message = formatter is not null
            ? formatter(state, exception)
            : state?.ToString() ?? string.Empty;

        var payload = new SentryEventPayload(
            Exception: ex,
            Message: message,
            Level: ToSentryLevel(logLevel),
            LoggerCategory: _category);

        _provider.Enqueue(payload);
    }

    private static string ToSentryLevel(LogLevel level) => level switch
    {
        LogLevel.Critical => "fatal",
        LogLevel.Error => "error",
        LogLevel.Warning => "warning",
        LogLevel.Information => "info",
        LogLevel.Debug => "debug",
        LogLevel.Trace => "debug",
        _ => "error",
    };
}

/// <summary>
/// Synthetic exception used only when a caller emits a
/// <see cref="LogLevel.Critical"/> log without attaching a real one.
/// Lets us still produce a Sentry Issue with a meaningful title.
/// </summary>
internal sealed class SentryCriticalLogException : Exception
{
    public SentryCriticalLogException(string message) : base(message) { }
}
