using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyAdventure.Infrastructure.Telemetry;
using Shouldly;

namespace MyAdventure.Integration.Tests;

public class SentryEnvelopeTests
{
    private const string TestDsn =
        "https://abc123@o4511444968079360.ingest.de.sentry.io/4511444969390160";

    [Fact]
    public void SentryDsn_EnvelopeEndpoint_HasExpectedShape()
    {
        var parsed = SentryDsn.Parse(TestDsn);

        parsed.EnvelopeEndpoint.ShouldBe(
            "https://o4511444968079360.ingest.de.sentry.io/api/4511444969390160/envelope/");
    }

    [Fact]
    public void Envelope_HasThreeNewlineDelimitedJsonLines()
    {
        var dsn = SentryDsn.Parse(TestDsn);
        var payload = MakePayload(new InvalidOperationException("test boom"));

        var bytes = SentryEnvelope.BuildExceptionEvent(
            payload, dsn,
            serviceName: "MyAdventure",
            serviceVersion: "1.2.3",
            environment: "test",
            now: new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero));

        var text = Encoding.UTF8.GetString(bytes);
        var parts = text.Split('\n');
        parts.Length.ShouldBeGreaterThanOrEqualTo(3);
        parts[0].ShouldNotBeNullOrEmpty();
        parts[1].ShouldNotBeNullOrEmpty();
        parts[2].ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Envelope_HeaderCarriesDsnEventIdAndSentAt()
    {
        var dsn = SentryDsn.Parse(TestDsn);
        var bytes = SentryEnvelope.BuildExceptionEvent(
            MakePayload(new Exception("x")), dsn,
            "MyAdventure", "1.0.0", "production",
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        var header = ParseHeader(bytes);

        header.GetProperty("event_id").GetString().ShouldNotBeNullOrEmpty();
        header.GetProperty("dsn").GetString().ShouldBe(TestDsn);
        header.GetProperty("sent_at").GetString()
            .ShouldStartWith("2026-01-02T03:04:05");
    }

    [Fact]
    public void Envelope_ItemHeaderDeclaresEventTypeAndLength()
    {
        var dsn = SentryDsn.Parse(TestDsn);
        var bytes = SentryEnvelope.BuildExceptionEvent(
            MakePayload(new Exception("x")), dsn,
            "MyAdventure", "1.0.0", "production", DateTimeOffset.UtcNow);

        var (_, itemHeader, payload) = ParseAllThree(bytes);

        itemHeader.GetProperty("type").GetString().ShouldBe("event");
        itemHeader.GetProperty("content_type").GetString().ShouldBe("application/json");
        itemHeader.GetProperty("length").GetInt32()
            .ShouldBe(payload.Bytes.Length);
    }

    [Fact]
    public void Envelope_EventPayloadCarriesExceptionInterface()
    {
        var dsn = SentryDsn.Parse(TestDsn);
        var ex = MakeRealException("boom!");

        var bytes = SentryEnvelope.BuildExceptionEvent(
            MakePayload(ex), dsn,
            "MyAdventure", "1.0.0", "production", DateTimeOffset.UtcNow);

        var (_, _, payload) = ParseAllThree(bytes);
        var exceptionInterface = payload.Json.GetProperty("exception");
        var values = exceptionInterface.GetProperty("values");

        values.GetArrayLength().ShouldBe(1);
        var first = values[0];
        first.GetProperty("type").GetString().ShouldBe(typeof(InvalidOperationException).FullName);
        first.GetProperty("value").GetString().ShouldBe("boom!");

        var frames = first.GetProperty("stacktrace").GetProperty("frames");
        frames.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Envelope_EventPayloadCarriesEnvironmentAndRelease()
    {
        var dsn = SentryDsn.Parse(TestDsn);
        var bytes = SentryEnvelope.BuildExceptionEvent(
            MakePayload(new Exception("x")), dsn,
            "MyAdventure", "9.8.7", "staging", DateTimeOffset.UtcNow);

        var (_, _, payload) = ParseAllThree(bytes);

        payload.Json.GetProperty("environment").GetString().ShouldBe("staging");
        payload.Json.GetProperty("release").GetString().ShouldBe("MyAdventure@9.8.7");
        payload.Json.GetProperty("platform").GetString().ShouldBe("csharp");
        payload.Json.GetProperty("level").GetString().ShouldBe("error");
        payload.Json.GetProperty("logger").GetString().ShouldBe("MyAdventure.TestCategory");
    }

    [Fact]
    public void Envelope_InnerExceptionsAreIncluded()
    {
        var inner = MakeRealException("inner");
        var outer = new InvalidOperationException("outer", inner);

        var dsn = SentryDsn.Parse(TestDsn);
        var bytes = SentryEnvelope.BuildExceptionEvent(
            MakePayload(outer), dsn,
            "MyAdventure", "1.0.0", "test", DateTimeOffset.UtcNow);

        var (_, _, payload) = ParseAllThree(bytes);
        var values = payload.Json.GetProperty("exception").GetProperty("values");

        values.GetArrayLength().ShouldBe(2);
        values[0].GetProperty("value").GetString().ShouldBe("inner");
        values[1].GetProperty("value").GetString().ShouldBe("outer");
    }

    [Theory]
    [InlineData(LogLevel.Trace, false, false)]
    [InlineData(LogLevel.Debug, false, false)]
    [InlineData(LogLevel.Information, false, false)]
    [InlineData(LogLevel.Warning, false, false)]
    [InlineData(LogLevel.Error, false, false)]
    [InlineData(LogLevel.Critical, false, true)]
    [InlineData(LogLevel.Trace, true, true)]
    [InlineData(LogLevel.Information, true, true)]
    [InlineData(LogLevel.Error, true, true)]
    [InlineData(LogLevel.Critical, true, true)]
    public void ShouldPromoteToIssue_FollowsExceptionFirstRule(
        LogLevel level, bool hasException, bool expected)
    {
        var exception = hasException ? new InvalidOperationException("x") : null;

        SentryEventLoggerProvider
            .ShouldPromoteToIssue(level, exception)
            .ShouldBe(expected);
    }

    [Fact]
    public async Task Provider_LogErrorWithException_PostsEnvelopeToSentry()
    {
        var capture = new CapturingHandler();
        using var http = new HttpClient(capture);
        var dsn = SentryDsn.Parse(TestDsn);

        using var provider = new SentryEventLoggerProvider(
            dsn, http,
            serviceName: "MyAdventure",
            serviceVersion: "1.0.0",
            environment: "test",
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero)));

        var logger = provider.CreateLogger("MyAdventure.GameEngine");
        logger.LogError(MakeRealException("kaboom"), "something failed: {Detail}", "details");

        var request = await capture.AwaitRequestAsync(TimeSpan.FromSeconds(2));

        request.ShouldNotBeNull();
        request!.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe(dsn.EnvelopeEndpoint);

        request.Headers.TryGetValues("X-Sentry-Auth", out var authValues).ShouldBeTrue();
        string.Join(',', authValues!).ShouldBe(dsn.AuthHeaderValue);

        var bodyBytes = capture.RequestBody!;
        var (envHeader, itemHeader, eventPayload) = ParseAllThree(bodyBytes);

        envHeader.GetProperty("dsn").GetString().ShouldBe(TestDsn);
        itemHeader.GetProperty("type").GetString().ShouldBe("event");
        eventPayload.Json.GetProperty("exception")
            .GetProperty("values")[0]
            .GetProperty("value").GetString().ShouldBe("kaboom");
    }

    [Fact]
    public async Task Provider_LogInformation_DoesNotPost()
    {
        var capture = new CapturingHandler();
        using var http = new HttpClient(capture);
        var dsn = SentryDsn.Parse(TestDsn);

        using var provider = new SentryEventLoggerProvider(
            dsn, http, "MyAdventure", "1.0.0", "test", new FixedTimeProvider(DateTimeOffset.UtcNow));

        var logger = provider.CreateLogger("MyAdventure.GameEngine");
        logger.LogInformation("benign info message");

        await Task.Delay(100, TestContext.Current.CancellationToken);

        capture.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Provider_HttpFailure_DoesNotThrowToCaller()
    {
        var failing = new ThrowingHandler();
        using var http = new HttpClient(failing);
        var dsn = SentryDsn.Parse(TestDsn);

        using var provider = new SentryEventLoggerProvider(
            dsn, http, "MyAdventure", "1.0.0", "test", new FixedTimeProvider(DateTimeOffset.UtcNow));

        var logger = provider.CreateLogger("MyAdventure.GameEngine");
        Should.NotThrow(() => logger.LogError(new Exception("x"), "x"));

        await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    private static InvalidOperationException MakeRealException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private static SentryEventPayload MakePayload(Exception ex) =>
        new(ex, "test message", "error", "MyAdventure.TestCategory");

    private static JsonElement ParseHeader(byte[] envelopeBytes)
    {
        var newlineIndex = Array.IndexOf(envelopeBytes, (byte)'\n');
        newlineIndex.ShouldBeGreaterThan(0);
        var slice = new byte[newlineIndex];
        Array.Copy(envelopeBytes, slice, newlineIndex);
        return JsonDocument.Parse(slice).RootElement.Clone();
    }

    private record ParsedPayload(JsonElement Json, byte[] Bytes);

    private static (JsonElement EnvHeader, JsonElement ItemHeader, ParsedPayload Payload)
        ParseAllThree(byte[] envelopeBytes)
    {
        var parts = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < envelopeBytes.Length && parts.Count < 3; i++)
        {
            if (envelopeBytes[i] == (byte)'\n')
            {
                var chunk = new byte[i - start];
                Array.Copy(envelopeBytes, start, chunk, 0, chunk.Length);
                if (chunk.Length > 0) parts.Add(chunk);
                start = i + 1;
            }
        }
        parts.Count.ShouldBe(3, "envelope must have 3 JSON lines");

        var envHeader = JsonDocument.Parse(parts[0]).RootElement.Clone();
        var itemHeader = JsonDocument.Parse(parts[1]).RootElement.Clone();
        var payloadJson = JsonDocument.Parse(parts[2]).RootElement.Clone();
        return (envHeader, itemHeader, new ParsedPayload(payloadJson, parts[2]));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpRequestMessage> _signal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[]? RequestBody { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            _signal.TrySetResult(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public async Task<HttpRequestMessage?> AwaitRequestAsync(TimeSpan timeout)
        {
            var winner = await Task.WhenAny(_signal.Task, Task.Delay(timeout));
            return winner == _signal.Task ? await _signal.Task : null;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated network failure");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
