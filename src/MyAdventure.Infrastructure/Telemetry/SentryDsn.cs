namespace MyAdventure.Infrastructure.Telemetry;

public sealed class SentryDsn
{
    public string Scheme { get; }
    public string PublicKey { get; }
    public string? SecretKey { get; }
    public string Host { get; }
    public int? Port { get; }
    public string ProjectId { get; }

    public bool IsOtlp => true;

    public string LogsEndpoint => $"{GetBaseUri()}/api/{ProjectId}/otlp/v1/logs";

    public string TracesEndpoint => $"{GetBaseUri()}/api/{ProjectId}/otlp/v1/traces";

    public string AuthHeader
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SecretKey))
            {
                return $"Sentry sentry_key={PublicKey}";
            }

            return $"Sentry sentry_key={PublicKey}, sentry_secret={SecretKey}";
        }
    }

    private SentryDsn(
        string scheme,
        string publicKey,
        string? secretKey,
        string host,
        int? port,
        string projectId)
    {
        Scheme = scheme;
        PublicKey = publicKey;
        SecretKey = secretKey;
        Host = host;
        Port = port;
        ProjectId = projectId;
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
        string? secretKey = null;

        if (publicKey.Contains(':'))
        {
            var split = publicKey.Split(':', 2);

            publicKey = split[0];
            secretKey = split[1];
        }

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new ArgumentException("DSN public key is missing.", nameof(dsn));
        }

        var projectId = uri.AbsolutePath.Trim('/');

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("DSN project id is missing.", nameof(dsn));
        }

        return new SentryDsn(
            scheme: uri.Scheme,
            publicKey: publicKey,
            secretKey: secretKey,
            host: uri.Host,
            port: uri.IsDefaultPort ? null : uri.Port,
            projectId: projectId);
    }

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

    private string GetBaseUri()
    {
        if (Port.HasValue)
        {
            return $"{Scheme}://{Host}:{Port.Value}";
        }

        return $"{Scheme}://{Host}";
    }
}
