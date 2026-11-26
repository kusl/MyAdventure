using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MyAdventure.Infrastructure.Telemetry;

internal static class SentryEnvelope
{
    public const string EventContentType = "application/json";

    private const string SdkName = "myadventure.otlp-envelope";
    private const byte Newline = (byte)'\n';

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
        var eventJson = Write(writer => WriteEvent(writer, payload, eventId, serviceName, serviceVersion, environment, now));

        var envelopeHeader = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("event_id", eventId);
            writer.WriteString("dsn", dsn.Raw);
            writer.WriteString("sent_at", now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            WriteSdk(writer, serviceVersion);
            writer.WriteEndObject();
        });

        var itemHeader = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "event");
            writer.WriteNumber("length", eventJson.Length);
            writer.WriteString("content_type", EventContentType);
            writer.WriteEndObject();
        });

        return [.. envelopeHeader, Newline, .. itemHeader, Newline, .. eventJson, Newline];
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            write(writer);
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteEvent(
        Utf8JsonWriter writer,
        SentryEventPayload payload,
        string eventId,
        string serviceName,
        string serviceVersion,
        string environment,
        DateTimeOffset now)
    {
        writer.WriteStartObject();
        writer.WriteString("event_id", eventId);
        writer.WriteNumber("timestamp", now.ToUnixTimeMilliseconds() / 1000.0);
        writer.WriteString("platform", "csharp");
        writer.WriteString("level", payload.Level);
        writer.WriteString("logger", payload.LoggerCategory);
        writer.WriteString("server_name", Environment.MachineName);
        writer.WriteString("environment", environment);
        writer.WriteString("release", $"{serviceName}@{serviceVersion}");

        writer.WriteStartObject("message");
        writer.WriteString("formatted", payload.Message);
        writer.WriteEndObject();

        writer.WriteStartObject("exception");
        writer.WriteStartArray("values");
        foreach (var exception in ExceptionChain(payload.Exception))
        {
            WriteException(writer, exception);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        WriteSdk(writer, serviceVersion);

        if (payload.Tags is { Count: > 0 } tags)
        {
            writer.WriteStartObject("tags");
            foreach (var (key, value) in tags)
            {
                writer.WriteString(key, value);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static IEnumerable<Exception> ExceptionChain(Exception outermost)
    {
        var chain = new Stack<Exception>();
        for (var current = outermost; current is not null; current = current.InnerException)
        {
            chain.Push(current);
        }
        return chain;
    }

    private static void WriteException(Utf8JsonWriter writer, Exception exception)
    {
        var type = exception.GetType();
        writer.WriteStartObject();
        writer.WriteString("type", type.FullName ?? type.Name);
        writer.WriteString("value", exception.Message);
        writer.WriteString("module", type.Namespace ?? string.Empty);

        writer.WriteStartObject("stacktrace");
        writer.WriteStartArray("frames");
        var frames = new StackTrace(exception, fNeedFileInfo: true).GetFrames();
        for (var i = frames.Length - 1; i >= 0; i--)
        {
            WriteFrame(writer, frames[i]);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteFrame(Utf8JsonWriter writer, StackFrame frame)
    {
        if (frame.GetMethod() is not { } method) return;

        var module = method.DeclaringType?.FullName ?? "<unknown>";
        writer.WriteStartObject();
        writer.WriteString("function", method.Name);
        writer.WriteString("module", module);
        writer.WriteBoolean("in_app", module.StartsWith("MyAdventure", StringComparison.Ordinal));

        if (frame.GetFileName() is { Length: > 0 } fileName)
        {
            writer.WriteString("filename", fileName);
            writer.WriteString("abs_path", fileName);
        }

        if (frame.GetFileLineNumber() is > 0 and var line)
        {
            writer.WriteNumber("lineno", line);
        }

        writer.WriteEndObject();
    }

    private static void WriteSdk(Utf8JsonWriter writer, string serviceVersion)
    {
        writer.WriteStartObject("sdk");
        writer.WriteString("name", SdkName);
        writer.WriteString("version", serviceVersion);
        writer.WriteEndObject();
    }
}

internal sealed record SentryEventPayload(
    Exception Exception,
    string Message,
    string Level,
    string LoggerCategory,
    IReadOnlyDictionary<string, string>? Tags = null);
