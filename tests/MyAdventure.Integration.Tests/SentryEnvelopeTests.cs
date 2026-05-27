using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyAdventure.Infrastructure.Telemetry;
using Shouldly;

namespace MyAdventure.Integration.Tests;

/// <summary>
/// Tests for the Sentry envelope path that surfaces exceptions as
/// Issues in the Sentry UI. These cover three layers:
/// <list type="number">
///   <item>The pure byte-builder (<see cref="SentryEnvelope"/>) —
///   verified by parsing the produced bytes back into JSON and
///   checking the shape matches Sentry's documented envelope format.</item>
///   <item>The DSN-derived endpoint URL — verified by parsing a
///   realistic DSN and asserting <see cref="SentryDsn.EnvelopeEndpoint"/>
///   matches the documented Sentry shape.</item>
///   <item>The full <see cref="SentryEventLoggerProvider"/> end-to-end
///   through a fake <see cref="HttpMessageHandler"/> that captures the
///   POST request — verifying that a <c>logger.LogError(ex, "...")</c>
///   actually produces an outbound HTTP POST to Sentry's envelope
///   endpoint with the expected headers and body.</item>
/// </list>
///
/// All tests run entirely in-process; no network IO. The provider's
/// fire-and-forget Send path is awaited indirectly via the fake
/// handler's TaskCompletionSource so the tests remain deterministic.
/// </summary>
public class SentryEnvelopeTests
{
    private const string TestDsn =
        "https://abc123@o4511444968079360.ingest.de.sentry.io/4511444969390160";

    // --- DSN endpoint shape ------------------------------------------------

    [Fact]
    public void SentryDsn_EnvelopeEndpoint_HasExpectedShape()
    {
        // Sentry's envelope endpoint URL is documented as
        // /api/{project_id}/envelope/ — with a trailing slash. Some
        // Sentry deployments redirect when the slash is missing, others
        // 404; we don't want to depend on either behaviour.
        var parsed = SentryDsn.Parse(TestDsn);

        parsed.EnvelopeEndpoint.ShouldBe(
            "https://o4511444968079360.ingest.de.sentry.io/api/4511444969390160/envelope/");
    }

    // --- Envelope byte builder ---------------------------------------------

    [Fact]
    public void Envelope_HasThreeNewlineDelimitedJsonLines()
    {
        // The Sentry envelope grammar requires
        // {envelope_header}\n{item_header}\n{item_payload}\n
        // — three JSON objects separated by newlines, with an optional
        // trailing newline. We always emit the trailing newline so the
        // wire format matches the SDK examples Sentry publishes.
        var dsn = SentryDsn.Parse(TestDsn);
        var payload = MakePayload(new InvalidOperationException("test boom"));

        var bytes = SentryEnvelope.BuildExceptionEvent(
            payload, dsn,
            serviceName: "MyAdventure",
            serviceVersion: "1.2.3",
            environment: "test",
            now: new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero));

        var text = Encoding.UTF8.GetString(bytes);
        // Split on \n; expect three non-empty lines (header, item header,
        // item payload) plus an empty trailing token from the final \n.
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
        // ISO 8601 with the 'Z' suffix (UTC), matching DateTime.UtcNow.ToString("o").
        header.GetProperty("sent_at").GetString()
            .ShouldStartWith("2026-01-02T03:04:05");
    }

    [Fact]
    public void Envelope_ItemHeaderDeclaresEventTypeAndLength()
    {
        // The item header MUST declare type=event so Sentry routes the
        // payload to the Issues pipeline. The length, while technically
        // optional per the spec, lets Relay parse without scanning for
        // newlines and is what every shipping SDK emits.
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
        // The Issue's title and grouping in Sentry are driven by the
        // exception's type, value, and stack trace — so all three must
        // make it into the payload.
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
        // Sentry uses environment + release to scope Issues; both are
        // visible in the UI as filterable tags. We populate them from
        // TelemetryOptions.Sentry.Environment and the assembly version
        // so an Issue can be traced back to a specific build.
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
        // .NET commonly wraps the real cause in TargetInvocationException
        // / AggregateException; if we only sent the outermost frame the
        // Issue would be miscategorised. Walk the chain and emit each.
        var inner = MakeRealException("inner");
        var outer = new InvalidOperationException("outer", inner);

        var dsn = SentryDsn.Parse(TestDsn);
        var bytes = SentryEnvelope.BuildExceptionEvent(
            MakePayload(outer), dsn,
            "MyAdventure", "1.0.0", "test", DateTimeOffset.UtcNow);

        var (_, _, payload) = ParseAllThree(bytes);
        var values = payload.Json.GetProperty("exception").GetProperty("values");

        // Sentry's exception interface lists exceptions chronologically:
        // innermost first, outermost last. We assert that order so the
        // Sentry UI displays the chain in the natural "X caused Y" form.
        values.GetArrayLength().ShouldBe(2);
        values[0].GetProperty("value").GetString().ShouldBe("inner");
        values[1].GetProperty("value").GetString().ShouldBe("outer");
    }

    // --- ShouldPromoteToIssue routing rule --------------------------------

    [Theory]
    [InlineData(LogLevel.Trace, false, false)]
    [InlineData(LogLevel.Debug, false, false)]
    [InlineData(LogLevel.Information, false, false)]
    [InlineData(LogLevel.Warning, false, false)]
    [InlineData(LogLevel.Error, false, false)]    // error w/o exception -> Logs panel only
    [InlineData(LogLevel.Critical, false, true)]  // critical always promotes
    [InlineData(LogLevel.Trace, true, true)]      // any level + exception -> Issue
    [InlineData(LogLevel.Information, true, true)]
    [InlineData(LogLevel.Error, true, true)]
    [InlineData(LogLevel.Critical, true, true)]
    public void ShouldPromoteToIssue_FollowsExceptionFirstRule(
        LogLevel level, bool hasException, bool expected)
    {
        // The intent is: every log record with an attached exception
        // becomes a Sentry Issue (that's what 99% of callers want).
        // Plain LogError without an exception stays in the Logs panel
        // — promoting it would flood the project with low-signal events
        // like EF Core's "First/FirstOrDefault without OrderBy" warning.
        var exception = hasException ? new InvalidOperationException("x") : null;

        SentryEventLoggerProvider
            .ShouldPromoteToIssue(level, exception)
            .ShouldBe(expected);
    }

    // --- End-to-end provider -> HTTP --------------------------------------

    [Fact]
    public async Task Provider_LogErrorWithException_PostsEnvelopeToSentry()
    {
        // Drive the real ILoggerProvider, capture the HTTP request it
        // produces, and verify the request's URL, headers, and body.
        // This is the proof that the wire that arrives at Sentry's
        // envelope endpoint actually carries an Issue-eligible event.
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

        // The send is fire-and-forget; wait briefly for the capturing
        // handler to receive the request. 2 seconds is generous; in
        // practice this completes in microseconds because the handler
        // is in-process.
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
        // Information-level records (which is what the game emits on
        // every business purchase, every save, etc.) MUST NOT be
        // promoted to Sentry Issues. Otherwise a single play session
        // would flood the Issues panel with hundreds of bogus "issues".
        var capture = new CapturingHandler();
        using var http = new HttpClient(capture);
        var dsn = SentryDsn.Parse(TestDsn);

        using var provider = new SentryEventLoggerProvider(
            dsn, http, "MyAdventure", "1.0.0", "test", new FixedTimeProvider(DateTimeOffset.UtcNow));

        var logger = provider.CreateLogger("MyAdventure.GameEngine");
        logger.LogInformation("benign info message");

        // Wait briefly; the absence of any captured request is the
        // assertion. Using a real (short) timeout rather than a busy
        // check avoids flakes on slow CI.
        await Task.Delay(100);

        capture.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Provider_HttpFailure_DoesNotThrowToCaller()
    {
        // Sentry could be down, the network could be unreachable, the
        // DSN could be valid-syntax-but-wrong-project; in every case
        // logging an exception must not turn into a second exception
        // crashing the caller. The provider swallows transport failures
        // by design.
        var failing = new ThrowingHandler();
        using var http = new HttpClient(failing);
        var dsn = SentryDsn.Parse(TestDsn);

        using var provider = new SentryEventLoggerProvider(
            dsn, http, "MyAdventure", "1.0.0", "test", new FixedTimeProvider(DateTimeOffset.UtcNow));

        var logger = provider.CreateLogger("MyAdventure.GameEngine");
        Should.NotThrow(() => logger.LogError(new Exception("x"), "x"));

        // Background Send task must also not produce an unobserved
        // exception that could trip the finalizer. Give it a moment
        // to complete and confirm we are still alive.
        await Task.Delay(50);
    }

    // --- Helpers ----------------------------------------------------------

    /// <summary>
    /// Build an exception that has a real stack trace by throwing and
    /// catching it. <see cref="StackTrace"/> on an unthrown exception
    /// is empty in .NET, so a synthetic <c>new InvalidOperationException()</c>
    /// would produce a stack-frame-less Sentry Issue.
    /// </summary>
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
        // Split on \n; the first three non-empty tokens are
        // envelope header, item header, and item payload respectively.
        // We don't use the length prefix here — the test parser walks
        // the bytes the same way a human would when eyeballing the wire
        // format, which catches mistakes the length-aware parser would
        // hide.
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

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that records the request
    /// (including its body) and returns a synthetic 200 OK response.
    /// Exposes a <see cref="Task"/>-based awaiter so tests can wait for
    /// the fire-and-forget background Send to land without polling.
    /// </summary>
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

    /// <summary>
    /// A handler that always throws. Used to confirm that
    /// transport-layer failures don't escape the provider's
    /// fire-and-forget catch block into the caller's logging code.
    /// </summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated network failure");
    }

    /// <summary>
    /// <see cref="TimeProvider"/> that returns a pinned instant — so
    /// tests can pin the envelope's <c>sent_at</c> and the event's
    /// <c>timestamp</c> field.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
