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

    public static bool TryParse(string dsn, out SentryDsn result)
    {
        if (string.IsNullOrWhiteSpace(dsn))
        {
            result = new SentryDsn(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty);
            return false;
        }

        try
        {
            result = Parse(dsn);
            return true;
        }
        catch
        {
            result = new SentryDsn(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty);
            return false;
        }
    }

    public static SentryDsn Parse(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return new SentryDsn(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty);
        }

        string publicKey = string.Empty;
        string secretKey = string.Empty;
        string host = string.Empty;
        string projectId = string.Empty;

        if (dsn.Contains(':') && dsn.Contains('@'))
        {
            try
            {
                var schemeSplit = dsn.Split("://", StringSplitOptions.None);
                var remainder = schemeSplit[1];

                var atSplit = remainder.Split('@');
                var keys = atSplit[0].Split(':');
                publicKey = keys[0];
                if (keys.Length > 1)
                {
                    secretKey = keys[1];
                }

                var pathSplit = atSplit[1].Split('/');
                host = pathSplit[0];
                projectId = pathSplit[1];
            }
            catch (Exception ex)
            {
                throw new ArgumentException("DSN is not a valid legacy or standard absolute URI.", nameof(dsn), ex);
            }
        }
        else
        {
            if (!Uri.TryCreate(dsn, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("DSN is not a valid absolute URI.", nameof(dsn));
            }

            host = uri.Host;
            publicKey = uri.UserInfo;

            var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathSegments.Length == 0)
            {
                throw new ArgumentException("DSN missing project ID metadata.", nameof(dsn));
            }
            projectId = pathSegments[0];
        }

        var isOtlp = true;
        var logsEndpoint = $"https://{host}/api/{projectId}/integration/otlp/v1/logs";
        var tracesEndpoint = $"https://{host}/api/{projectId}/integration/otlp/v1/traces";
        var authHeaderValue = $"x-sentry-auth=sentry sentry_key={publicKey}";

        return new SentryDsn(
            dsn,
            publicKey,
            secretKey,
            projectId,
            host,
            isOtlp,
            logsEndpoint,
            tracesEndpoint,
            authHeaderValue);
    }
}
