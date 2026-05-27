using System;

namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// A parsed Sentry DSN with the three endpoint URLs the project needs
/// derived from it:
///
/// <list type="bullet">
///   <item><see cref="LogsEndpoint"/> — Sentry's OTLP/HTTP logs intake
///   (populates the project's "Logs" panel).</item>
///   <item><see cref="TracesEndpoint"/> — Sentry's OTLP/HTTP traces
///   intake (populates the project's "Traces" panel).</item>
///   <item><see cref="EnvelopeEndpoint"/> — Sentry's classic envelope
///   intake (populates the project's "Issues" panel; this is the only
///   one of the three that creates Issues from exceptions).</item>
/// </list>
///
/// <para>
/// <b>Why all three.</b> Sentry's OTLP intake is documented as
/// "open beta" and does not generate Issues from OTLP logs nor from
/// span exception events (those events are dropped during ingestion).
/// To get an error to show up under "Issues" we have to send it as a
/// classic Sentry event envelope to <see cref="EnvelopeEndpoint"/>. The
/// OTLP endpoints are still useful for the Logs and Traces panels, so
/// we register both side-by-side rather than picking one.
/// </para>
/// </summary>
public sealed class SentryDsn
{
    public string Raw { get; }
    public string PublicKey { get; }
    public string SecretKey { get; }
    public string ProjectId { get; }
    public string Host { get; }
    public bool IsOtlp { get; }
    public string LogsEndpoint { get; }
    public string TracesEndpoint { get; }

    /// <summary>
    /// Sentry's classic envelope endpoint:
    /// <c>{scheme}://{host}/api/{projectId}/envelope/</c>. Posting an
    /// envelope with an <c>event</c> item carrying an <c>exception</c>
    /// interface creates an Issue under "Issues" in the Sentry UI. This
    /// is the route the project uses for surfacing exceptions; the OTLP
    /// logs endpoint is unsuitable for that purpose (per Sentry's docs
    /// it does not create Issues).
    /// </summary>
    public string EnvelopeEndpoint { get; }

    /// <summary>
    /// The value for the <c>x-sentry-auth</c> HTTP header. Sentry's
    /// documented OTLP intake format is
    /// <c>sentry sentry_key={publicKey}</c> — a single space-separated
    /// token list, <b>no commas</b>. This is also accepted at the
    /// classic envelope endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The comma matters.</b> The OpenTelemetry .NET OTLP exporter
    /// parses its <c>Headers</c> string by splitting on <c>','</c> to
    /// support setting multiple headers in one go (per the
    /// OpenTelemetry spec). If we embed a comma inside the auth value,
    /// the exporter mis-splits it and the project key gets sent under
    /// a header named "sentry_key" while <c>x-sentry-auth</c> arrives
    /// without it. Sentry then drops the event silently — no 4xx, no
    /// log line, no Issue. This is exactly the failure mode that left
    /// the project's Sentry dashboard empty in May 2026.
    /// </para>
    /// <para>
    /// Earlier versions of this code emitted
    /// <c>sentry sentry_version=7, sentry_key={publicKey}</c> from
    /// a misreading of legacy Sentry SDK auth-header docs. The
    /// <c>sentry_version</c> field belongs to the deprecated
    /// <c>/store/</c> endpoint and is not required (or honoured) by
    /// the OTLP intake or the modern envelope intake; dropping it
    /// also removes the comma that was breaking OTLP delivery.
    /// </para>
    /// </remarks>
    public string AuthHeaderValue { get; }

    private SentryDsn(
        string raw,
        string publicKey,
        string secretKey,
        string projectId,
        string host,
        bool isOtlp,
        string logsEndpoint,
        string tracesEndpoint,
        string envelopeEndpoint,
        string authHeaderValue)
    {
        Raw = raw;
        PublicKey = publicKey;
        SecretKey = secretKey;
        ProjectId = projectId;
        Host = host;
        IsOtlp = isOtlp;
        LogsEndpoint = logsEndpoint;
        TracesEndpoint = tracesEndpoint;
        EnvelopeEndpoint = envelopeEndpoint;
        AuthHeaderValue = authHeaderValue;
    }

    /// <summary>
    /// Non-throwing parser. Accepts <see langword="null"/>, whitespace, and
    /// malformed input alongside well-formed DSNs — all problem cases
    /// return <see langword="false"/> with a populated
    /// <paramref name="error"/>. This is the standard TryParse contract:
    /// never throw, always produce a verdict.
    /// </summary>
    public static bool TryParse(
        string? dsn,
        out SentryDsn? parsed,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(dsn))
        {
            parsed = null;
            error = "DSN is empty.";
            return false;
        }

        try
        {
            parsed = Parse(dsn);
            error = null;
            return parsed.IsOtlp;
        }
        catch (Exception ex)
        {
            parsed = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Throwing parser. Use <see cref="TryParse"/> when validating user
    /// input; use this directly only when the DSN has already been
    /// validated and a malformed string should crash loudly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Handles both the modern Sentry DSN form
    /// <c>https://{public_key}@{host}/{project_id}</c> and the legacy
    /// form <c>https://{public_key}:{secret_key}@{host}/{project_id}</c>.
    /// The colon in the legacy form would otherwise make
    /// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> reject the
    /// string (the parser treats the part after the colon as a port and
    /// fails on the non-numeric secret), so we pre-split the userinfo
    /// before constructing the <see cref="Uri"/>.
    /// </para>
    /// <para>
    /// Only <c>http</c> and <c>https</c> are accepted — Sentry DSNs are
    /// always one of those, and an <c>ftp://</c> or other-scheme value
    /// is almost certainly a copy-paste error that we want to surface
    /// loudly rather than silently emit telemetry into a black hole.
    /// </para>
    /// </remarks>
    public static SentryDsn Parse(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn))
        {
            throw new ArgumentException("DSN is empty.", nameof(dsn));
        }

        // Strip a legacy "publicKey:secretKey@" userinfo block before
        // letting Uri.TryCreate see it. Uri.TryCreate would otherwise
        // try to parse the secretKey as a port number and reject the
        // whole DSN.
        var (uriCandidate, secretKey) = StripLegacySecretKey(dsn);

        if (!Uri.TryCreate(uriCandidate, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("DSN is not a valid absolute URI.", nameof(dsn));
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"DSN scheme '{uri.Scheme}' is not supported. Sentry DSNs must be http or https.",
                nameof(dsn));
        }

        var publicKey = uri.UserInfo;

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new ArgumentException("DSN public key is missing.", nameof(dsn));
        }

        var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Length == 0)
        {
            throw new ArgumentException("DSN missing project ID metadata.", nameof(dsn));
        }

        var projectId = pathSegments[0];

        var host = uri.IsDefaultPort
            ? uri.Host
            : $"{uri.Host}:{uri.Port}";

        var baseUri = $"{uri.Scheme}://{host}";

        // Sentry's OTLP intake lives under /api/{projectId}/integration/otlp/v1/{logs,traces}.
        // The "/integration" segment is required — without it Sentry returns 404
        // and silently drops every event, which is exactly the failure mode
        // that motivated this parser's existence.
        var logsEndpoint =
            $"{baseUri}/api/{projectId}/integration/otlp/v1/logs";

        var tracesEndpoint =
            $"{baseUri}/api/{projectId}/integration/otlp/v1/traces";

        // Sentry's classic envelope endpoint lives under /api/{projectId}/envelope/
        // — note the trailing slash, which is part of the documented URL shape
        // (omitting it triggers a 308 redirect on some Sentry deployments and
        // a hard 404 on others; we don't want to depend on either behaviour).
        var envelopeEndpoint =
            $"{baseUri}/api/{projectId}/envelope/";

        // Sentry's documented OTLP auth header value (see
        // docs.sentry.io/concepts/otlp/direct/logs and
        // .../direct/traces) is the space-separated form:
        //
        //     sentry sentry_key={publicKey}
        //
        // — NO commas, NO sentry_version field. We honoured the legacy
        // secret key from the colon-form DSN by appending it as
        // `sentry_secret={secret}`, also space-separated. Sentry's
        // intake accepts the secret if present but does not require
        // it for modern DSNs.
        //
        // The single space between tokens is what the documented format
        // uses; the absence of commas is what keeps the OpenTelemetry
        // .NET OTLP exporter from mis-splitting the value on its way
        // into the HTTP request (see AuthHeaderValue XML doc).
        var authHeaderValue = string.IsNullOrWhiteSpace(secretKey)
            ? $"sentry sentry_key={publicKey}"
            : $"sentry sentry_key={publicKey} sentry_secret={secretKey}";

        return new SentryDsn(
            raw: dsn,
            publicKey: publicKey,
            secretKey: secretKey,
            projectId: projectId,
            host: host,
            isOtlp: true,
            logsEndpoint: logsEndpoint,
            tracesEndpoint: tracesEndpoint,
            envelopeEndpoint: envelopeEndpoint,
            authHeaderValue: authHeaderValue);
    }

    /// <summary>
    /// If <paramref name="dsn"/> uses the legacy
    /// <c>scheme://publicKey:secretKey@host/...</c> form, rewrite it to
    /// <c>scheme://publicKey@host/...</c> and return the extracted
    /// secret. Otherwise return the input unchanged and an empty secret.
    /// </summary>
    private static (string Rewritten, string SecretKey) StripLegacySecretKey(string dsn)
    {
        var schemeSeparator = dsn.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return (dsn, string.Empty);
        }

        var userInfoStart = schemeSeparator + 3;
        var atSign = dsn.IndexOf('@', userInfoStart);
        if (atSign < 0)
        {
            return (dsn, string.Empty);
        }

        var userInfo = dsn.Substring(userInfoStart, atSign - userInfoStart);
        var colon = userInfo.IndexOf(':');
        if (colon < 0)
        {
            return (dsn, string.Empty);
        }

        var publicKey = userInfo.Substring(0, colon);
        var secretKey = userInfo.Substring(colon + 1);

        var rewritten =
            dsn.Substring(0, userInfoStart) + publicKey + dsn.Substring(atSign);
        return (rewritten, secretKey);
    }
}
