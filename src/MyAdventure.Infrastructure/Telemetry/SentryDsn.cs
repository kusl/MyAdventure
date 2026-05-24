using System.Diagnostics.CodeAnalysis;

namespace MyAdventure.Infrastructure.Telemetry;

/// <summary>
/// Parses a Sentry DSN string into the pieces required to talk to its
/// hosted OTLP endpoints over plain HTTP/protobuf — without taking a
/// dependency on the Sentry .NET SDK.
///
/// <para>
/// A Sentry DSN looks like
/// <c>https://&lt;publicKey&gt;@o&lt;orgId&gt;.ingest.&lt;region&gt;.sentry.io/&lt;projectId&gt;</c>.
/// The first path segment is the project id; the host's first label
/// (<c>o&lt;orgId&gt;</c>) carries the org id; the userinfo is the
/// public key. Sentry's documented OTLP URL shape is
/// <c>https://&lt;host&gt;/api/&lt;projectId&gt;/integration/otlp/v1/{traces|logs}</c>
/// and the auth header is <c>x-sentry-auth: sentry sentry_key=&lt;publicKey&gt;</c>.
/// </para>
///
/// <para>
/// Region-aware: <c>ingest.sentry.io</c>, <c>ingest.us.sentry.io</c>,
/// <c>ingest.de.sentry.io</c>, and any future region are all handled
/// the same way (we re-use the host that was given to us, only deriving
/// the path and headers).
/// </para>
/// </summary>
public sealed class SentryDsn
{
    /// <summary>The public key (DSN userinfo).</summary>
    public required string PublicKey { get; init; }

    /// <summary>The project id (first path segment of the DSN).</summary>
    public required string ProjectId { get; init; }

    /// <summary>The ingest host (e.g. <c>o123.ingest.de.sentry.io</c>).</summary>
    public required string Host { get; init; }

    /// <summary>The full OTLP traces endpoint URL.</summary>
    public Uri TracesEndpoint =>
        new($"https://{Host}/api/{ProjectId}/integration/otlp/v1/traces");

    /// <summary>The full OTLP logs endpoint URL.</summary>
    public Uri LogsEndpoint =>
        new($"https://{Host}/api/{ProjectId}/integration/otlp/v1/logs");

    /// <summary>
    /// The value to put in the <c>x-sentry-auth</c> header. The header
    /// <i>name</i> is fixed ("x-sentry-auth"); only this value changes
    /// per-project.
    /// </summary>
    public string AuthHeaderValue => $"sentry sentry_key={PublicKey}";

    /// <summary>
    /// Parse the given DSN. Returns <c>false</c> and a descriptive
    /// <paramref name="error"/> on malformed input rather than throwing
    /// — config errors should be reported at startup, not crash the
    /// game. The caller can log the error and proceed with telemetry
    /// disabled.
    /// </summary>
    public static bool TryParse(
        string? dsn,
        [NotNullWhen(true)] out SentryDsn? result,
        out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(dsn))
        {
            error = "DSN is empty.";
            return false;
        }

        if (!Uri.TryCreate(dsn.Trim(), UriKind.Absolute, out var uri))
        {
            error = "DSN is not a valid absolute URI.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"DSN scheme '{uri.Scheme}' is not http(s).";
            return false;
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "DSN is missing the public key (the part before '@').";
            return false;
        }

        // UserInfo can be "publicKey" or (legacy DSNs) "publicKey:secretKey".
        // Sentry's OTLP auth wants only the public key.
        var publicKey = uri.UserInfo;
        var colon = publicKey.IndexOf(':');
        if (colon >= 0) publicKey = publicKey[..colon];

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            error = "DSN public key is empty.";
            return false;
        }

        // First path segment is the project id. AbsolutePath starts with '/'.
        var projectId = uri.AbsolutePath.Trim('/').Split('/', 2)[0];
        if (string.IsNullOrWhiteSpace(projectId))
        {
            error = "DSN is missing the project id (the path segment after the host).";
            return false;
        }

        result = new SentryDsn
        {
            PublicKey = publicKey,
            ProjectId = projectId,
            Host = uri.Host,
        };
        return true;
    }

    /// <summary>
    /// Convenience wrapper around <see cref="TryParse"/> that throws on
    /// invalid input. Useful in unit tests where any error is a bug.
    /// </summary>
    public static SentryDsn Parse(string dsn)
    {
        if (!TryParse(dsn, out var result, out var error))
            throw new ArgumentException(error, nameof(dsn));
        return result;
    }
}
