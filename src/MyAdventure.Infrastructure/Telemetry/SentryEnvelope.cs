using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// Builds the wire-format payload that
/// <see cref="SentryEventLoggerProvider"/> posts to Sentry's classic
/// envelope endpoint (<c>POST /api/{projectId}/envelope/</c>).
///
/// <para>
/// <b>Why this exists.</b> Sentry's OTLP logs endpoint deposits logs
/// into the "Logs" panel but does <i>not</i> generate Issues from
/// error-severity log records. Sentry's OTLP traces endpoint also
/// drops span exception events during ingestion. The only path that
/// produces a real Sentry Issue from a .NET exception — without
/// dragging in the vendor-specific Sentry .NET SDK — is to POST a
/// classic Sentry envelope containing an <c>event</c> item whose
/// payload uses the <c>exception</c> interface. That's exactly what
/// this class produces.
/// </para>
///
/// <para>
/// The envelope grammar (newline-separated JSON lines) is defined in
/// <c>develop.sentry.dev/sdk/data-model/envelopes</c>:
/// <code>
/// {envelope_header}\n
/// {item_header}\n
/// {item_payload}\n
/// </code>
/// We keep the output deterministic so the tests can pin the exact
/// bytes that go over the wire.
/// </para>
///
/// <para>
/// Pure: no IO, no clocks injected from outside, only the data passed
/// in. <see cref="SentryEventLoggerProvider"/> supplies the current
/// time via <see cref="TimeProvider"/> so the envelope and event
/// timestamps can be pinned in tests.
/// </para>
/// </summary>
internal static class SentryEnvelope
{
    /// <summary>
    /// The single content type used for both the envelope header and
    /// every event item we emit. Sentry accepts <c>application/json</c>
    /// for event payloads.
    /// </summary>
    public const string EventContentType = "application/json";

    /// <summary>
    /// Build the raw bytes of a Sentry envelope carrying a single
    /// <c>event</c> item that describes an exception. The bytes are
    /// what gets POSTed to <see cref="SentryDsn.EnvelopeEndpoint"/>.
    /// </summary>
    /// <param name="payload">The exception + metadata captured from
    /// the originating log record.</param>
    /// <param name="dsn">Parsed DSN; only the raw string is embedded
    /// in the envelope header (Sentry uses it as a self-authentication
    /// hint).</param>
    /// <param name="serviceName">Resource attribute — copied straight
    /// from <see cref="OpenTelemetry.Resources.ResourceBuilder"/>.</param>
    /// <param name="serviceVersion">Resource attribute.</param>
    /// <param name="environment">Resource attribute — typically
    /// "production", "staging", "development".</param>
    /// <param name="now">The current UTC time; injected so tests can
    /// pin the timestamps in the produced bytes.</param>
    public static byte[] BuildExceptionEvent(
        SentryEventPayload payload,
        SentryDsn dsn,
        string serviceName,
        string serviceVersion,
        string environment,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(dsn);

        var eventId = Guid.NewGuid().ToString("N");
        var sentAt = now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

        var envelopeHeader = new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["dsn"] = dsn.Raw,
            ["sent_at"] = sentAt,
            ["sdk"] = new Dictionary<string, object?>
            {
                // We are not the Sentry .NET SDK; identify ourselves
                // honestly so Sentry doesn't apply SDK-specific
                // processing rules meant for the real client.
                ["name"] = "myadventure.otlp-envelope",
                ["version"] = serviceVersion,
            },
        };

        var eventPayload = BuildEventPayload(
            payload, eventId, serviceName, serviceVersion, environment, now);

        // Serialise both halves to JSON with the same options so the
        // wire bytes are deterministic across .NET runtimes.
        var headerJson = JsonSerializer.SerializeToUtf8Bytes(envelopeHeader, JsonOpts);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(eventPayload, JsonOpts);

        var itemHeader = new Dictionary<string, object?>
        {
            ["type"] = "event",
            ["length"] = payloadJson.Length,
            ["content_type"] = EventContentType,
        };
        var itemHeaderJson = JsonSerializer.SerializeToUtf8Bytes(itemHeader, JsonOpts);

        // Stitch: header \n item_header \n item_payload \n
        // The trailing newline is optional per the spec but Sentry's
        // examples include it, and including it costs us nothing.
        var newline = (byte)'\n';
        using var buffer = new System.IO.MemoryStream(
            headerJson.Length + itemHeaderJson.Length + payloadJson.Length + 3);
        buffer.Write(headerJson, 0, headerJson.Length);
        buffer.WriteByte(newline);
        buffer.Write(itemHeaderJson, 0, itemHeaderJson.Length);
        buffer.WriteByte(newline);
        buffer.Write(payloadJson, 0, payloadJson.Length);
        buffer.WriteByte(newline);
        return buffer.ToArray();
    }

    private static Dictionary<string, object?> BuildEventPayload(
        SentryEventPayload payload,
        string eventId,
        string serviceName,
        string serviceVersion,
        string environment,
        DateTimeOffset now)
    {
        // Sentry's event timestamp is "seconds since the Unix epoch as
        // a floating-point number" — see Event Payloads docs. Strings
        // in RFC 3339 are also accepted but the float form is what the
        // Sentry .NET SDK emits and what the documented examples use.
        var timestampSeconds = now.ToUnixTimeMilliseconds() / 1000.0;

        var dict = new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["timestamp"] = timestampSeconds,
            ["platform"] = "csharp",
            ["level"] = payload.Level,
            ["logger"] = payload.LoggerCategory,
            ["server_name"] = Environment.MachineName,
            ["environment"] = environment,
            ["release"] = $"{serviceName}@{serviceVersion}",
            ["message"] = new Dictionary<string, object?>
            {
                ["formatted"] = payload.Message,
            },
            ["exception"] = new Dictionary<string, object?>
            {
                ["values"] = BuildExceptionValues(payload),
            },
            ["sdk"] = new Dictionary<string, object?>
            {
                ["name"] = "myadventure.otlp-envelope",
                ["version"] = serviceVersion,
            },
        };

        if (payload.Tags is { Count: > 0 })
        {
            dict["tags"] = payload.Tags;
        }

        return dict;
    }

    private static List<Dictionary<string, object?>> BuildExceptionValues(
        SentryEventPayload payload)
    {
        // Walk the exception chain from outermost to innermost. Sentry's
        // exception interface accepts the values list in *chronological*
        // order — innermost first — so we reverse at the end. The grouping
        // logic on Sentry's side uses the outermost frame in that order to
        // produce the issue title.
        var chain = new List<Exception>();
        var current = payload.Exception;
        while (current is not null)
        {
            chain.Add(current);
            current = current.InnerException;
        }
        chain.Reverse();

        var values = new List<Dictionary<string, object?>>(chain.Count);
        foreach (var ex in chain)
        {
            values.Add(new Dictionary<string, object?>
            {
                ["type"] = ex.GetType().FullName ?? ex.GetType().Name,
                ["value"] = ex.Message ?? string.Empty,
                ["module"] = ex.GetType().Namespace ?? string.Empty,
                ["stacktrace"] = BuildStacktrace(ex),
            });
        }
        return values;
    }

    private static Dictionary<string, object?> BuildStacktrace(Exception ex)
    {
        var frames = new List<Dictionary<string, object?>>();
        var stackTrace = new StackTrace(ex, fNeedFileInfo: true);
        var stackFrames = stackTrace.GetFrames();

        // Sentry's stack trace interface lists frames in *call order* —
        // the outermost frame first, the throw site last. .NET's
        // StackTrace.GetFrames() returns the opposite (throw site first),
        // so we reverse.
        for (var i = stackFrames.Length - 1; i >= 0; i--)
        {
            var frame = stackFrames[i];
            var method = frame.GetMethod();
            if (method is null) continue;

            var declaringType = method.DeclaringType;
            var moduleName = declaringType?.FullName ?? "<unknown>";
            var functionName = method.Name;
            var fileName = frame.GetFileName();
            var lineNumber = frame.GetFileLineNumber();

            var frameDict = new Dictionary<string, object?>
            {
                ["function"] = functionName,
                ["module"] = moduleName,
                ["in_app"] = IsInApp(moduleName),
            };

            if (!string.IsNullOrEmpty(fileName))
            {
                frameDict["filename"] = fileName;
                frameDict["abs_path"] = fileName;
            }
            if (lineNumber > 0)
            {
                frameDict["lineno"] = lineNumber;
            }

            frames.Add(frameDict);
        }

        return new Dictionary<string, object?>
        {
            ["frames"] = frames,
        };
    }

    /// <summary>
    /// Mark frames belonging to our own assemblies as "in-app" so the
    /// Sentry UI highlights them; frames from System.*, Microsoft.*,
    /// OpenTelemetry.* etc. show up as library frames and are folded
    /// away by default. The "in_app" flag is what drives that grouping.
    /// </summary>
    private static bool IsInApp(string moduleName)
    {
        return moduleName.StartsWith("MyAdventure", StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Compact form (no indentation) keeps the over-the-wire envelope
        // small and matches the format Sentry's own SDKs emit.
        WriteIndented = false,
        // The Sentry envelope spec mandates UTF-8 with valid JSON; do not
        // emit \uXXXX escapes for non-ASCII characters that don't need
        // escaping.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// Bag of fields extracted from an <see cref="Microsoft.Extensions.Logging.ILogger"/>
/// invocation that the envelope builder needs to serialise an exception
/// event. Construction is deliberately cheap so we can build one per
/// triggering log record without measurable overhead.
/// </summary>
internal sealed record SentryEventPayload(
    Exception Exception,
    string Message,
    string Level,
    string LoggerCategory,
    IReadOnlyDictionary<string, string>? Tags = null);
