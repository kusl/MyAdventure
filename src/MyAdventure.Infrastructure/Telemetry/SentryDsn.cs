using System;

namespace MyAdventure.Infrastructure.Telemetry;

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

        // Sentry's documented OTLP auth header value uses a lowercase
        // "sentry" keyword (see Settings → Client Keys in the Sentry UI).
        // The legacy public/secret form includes both keys; the modern
        // form omits sentry_secret entirely.
        string authHeaderValue;

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            authHeaderValue = $"sentry sentry_version=7, sentry_key={publicKey}";
        }
        else
        {
            authHeaderValue = $"sentry sentry_version=7, sentry_key={publicKey}, sentry_secret={secretKey}";
        }

        return new SentryDsn(
            raw: dsn,
            publicKey: publicKey,
            secretKey: secretKey,
            projectId: projectId,
            host: host,
            isOtlp: true,
            logsEndpoint: logsEndpoint,
            tracesEndpoint: tracesEndpoint,
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
