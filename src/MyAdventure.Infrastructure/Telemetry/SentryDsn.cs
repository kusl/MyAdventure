using System.Diagnostics.CodeAnalysis;

namespace MyAdventure.Infrastructure.Telemetry;

public sealed record SentryDsn
{
    public required string Raw { get; init; }
    public required string PublicKey { get; init; }
    public required string SecretKey { get; init; }
    public required string ProjectId { get; init; }
    public required string Host { get; init; }
    public required string LogsEndpoint { get; init; }
    public required string TracesEndpoint { get; init; }
    public required string EnvelopeEndpoint { get; init; }
    public required string AuthHeaderValue { get; init; }

    public static bool TryParse(string? dsn, [NotNullWhen(true)] out SentryDsn? parsed, out string? error)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(dsn))
        {
            error = "DSN is empty.";
            return false;
        }

        try
        {
            parsed = Parse(dsn);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static SentryDsn Parse(string dsn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dsn);

        var (uriCandidate, secretKey) = StripLegacySecretKey(dsn);

        if (!Uri.TryCreate(uriCandidate, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("DSN is not a valid absolute URI.", nameof(dsn));
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException($"DSN scheme '{uri.Scheme}' is not supported. Sentry DSNs must be http or https.", nameof(dsn));
        }

        var publicKey = uri.UserInfo;
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new ArgumentException("DSN public key is missing.", nameof(dsn));
        }

        var projectId = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? throw new ArgumentException("DSN missing project ID metadata.", nameof(dsn));

        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var apiBase = $"{uri.Scheme}://{host}/api/{projectId}";

        return new SentryDsn
        {
            Raw = dsn,
            PublicKey = publicKey,
            SecretKey = secretKey,
            ProjectId = projectId,
            Host = host,
            LogsEndpoint = $"{apiBase}/integration/otlp/v1/logs",
            TracesEndpoint = $"{apiBase}/integration/otlp/v1/traces",
            EnvelopeEndpoint = $"{apiBase}/envelope/",
            AuthHeaderValue = string.IsNullOrWhiteSpace(secretKey)
                ? $"sentry sentry_key={publicKey}"
                : $"sentry sentry_key={publicKey} sentry_secret={secretKey}",
        };
    }

    private static (string Rewritten, string SecretKey) StripLegacySecretKey(string dsn)
    {
        var schemeSeparator = dsn.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0) return (dsn, string.Empty);

        var userInfoStart = schemeSeparator + 3;
        var atSign = dsn.IndexOf('@', userInfoStart);
        if (atSign < 0) return (dsn, string.Empty);

        var userInfo = dsn.AsSpan(userInfoStart, atSign - userInfoStart);
        var colon = userInfo.IndexOf(':');
        if (colon < 0) return (dsn, string.Empty);

        return (string.Concat(dsn.AsSpan(0, userInfoStart), userInfo[..colon], dsn.AsSpan(atSign)), userInfo[(colon + 1)..].ToString());
    }
}
