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

    public static bool TryParse(
        string dsn,
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

    public static SentryDsn Parse(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn))
        {
            throw new ArgumentException("DSN is empty.", nameof(dsn));
        }

        if (!Uri.TryCreate(dsn, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("DSN is not a valid absolute URI.", nameof(dsn));
        }

        var publicKey = uri.UserInfo;
        var secretKey = string.Empty;

        if (publicKey.Contains(':'))
        {
            var split = publicKey.Split(':', 2);
            publicKey = split[0];

            if (split.Length > 1)
            {
                secretKey = split[1];
            }
        }

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

        var logsEndpoint =
            $"{baseUri}/api/{projectId}/otlp/v1/logs";

        var tracesEndpoint =
            $"{baseUri}/api/{projectId}/otlp/v1/traces";

        string authHeaderValue;

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            authHeaderValue =
                $"Sentry sentry_key={publicKey}";
        }
        else
        {
            authHeaderValue =
                $"Sentry sentry_key={publicKey}, sentry_secret={secretKey}";
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
}
