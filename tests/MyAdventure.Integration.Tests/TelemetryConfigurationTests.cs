using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Telemetry;
using Shouldly;

namespace MyAdventure.Integration.Tests;

/// <summary>
/// Tests for the telemetry / Sentry-via-OTLP configuration plumbing. These
/// live under Integration.Tests rather than Core.Tests because they
/// exercise <see cref="DependencyInjection.AddInfrastructure(IServiceCollection,
/// TelemetryOptions, string?)"/> end-to-end and verify that the IoC
/// container actually builds with the new code paths — that's an
/// integration concern, not a unit-test concern.
/// </summary>
public class TelemetryConfigurationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly List<string> _envVarsToRestore = new();

    public TelemetryConfigurationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"myadventure-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        // Restore env vars that any individual test set, so subsequent
        // tests start from a clean baseline.
        foreach (var name in _envVarsToRestore)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
        }
    }

    private void SetEnv(string name, string? value)
    {
        if (!_envVarsToRestore.Contains(name)) _envVarsToRestore.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    // --- SentryDsn parser ---------------------------------------------------

    [Fact]
    public void SentryDsn_TryParse_RealisticDsn_PopulatesAllFields()
    {
        const string dsn =
            "https://fe6ae5ee15285c313b8171bb7a5a4ad0@o4511444968079360.ingest.de.sentry.io/4511444969390160";

        var ok = SentryDsn.TryParse(dsn, out var parsed, out var err);

        ok.ShouldBeTrue(err);
        parsed.ShouldNotBeNull();
        parsed.PublicKey.ShouldBe("fe6ae5ee15285c313b8171bb7a5a4ad0");
        parsed.ProjectId.ShouldBe("4511444969390160");
        parsed.Host.ShouldBe("o4511444968079360.ingest.de.sentry.io");
    }

    [Fact]
    public void SentryDsn_TracesEndpoint_HasExpectedShape()
    {
        const string dsn =
            "https://fe6ae5ee15285c313b8171bb7a5a4ad0@o4511444968079360.ingest.de.sentry.io/4511444969390160";

        var parsed = SentryDsn.Parse(dsn);

        parsed.TracesEndpoint.ToString().ShouldBe(
            "https://o4511444968079360.ingest.de.sentry.io/api/4511444969390160/integration/otlp/v1/traces");
    }

    [Fact]
    public void SentryDsn_LogsEndpoint_HasExpectedShape()
    {
        const string dsn =
            "https://fe6ae5ee15285c313b8171bb7a5a4ad0@o4511444968079360.ingest.de.sentry.io/4511444969390160";

        var parsed = SentryDsn.Parse(dsn);

        parsed.LogsEndpoint.ToString().ShouldBe(
            "https://o4511444968079360.ingest.de.sentry.io/api/4511444969390160/integration/otlp/v1/logs");
    }

    [Fact]
    public void SentryDsn_AuthHeader_StartsWithSentryKeyword()
    {
        const string dsn =
            "https://abc123@o123.ingest.us.sentry.io/456";
        var parsed = SentryDsn.Parse(dsn);
        parsed.AuthHeaderValue.ShouldBe("sentry sentry_key=abc123");
    }

    [Fact]
    public void SentryDsn_TryParse_HandlesLegacyPublicSecretKeyFormat()
    {
        // Old-style DSNs included a secret key after a colon. Sentry's
        // OTLP only wants the public key — the parser must strip the
        // secret portion silently rather than treating it as part of the
        // key.
        const string dsn = "https://[email protected]/9";
        var parsed = SentryDsn.Parse(dsn);
        parsed.PublicKey.ShouldBe("pubkey");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-url")]
    [InlineData("ftp://x@example.com/1")]            // wrong scheme
    [InlineData("https://example.com/1")]            // no public key
    [InlineData("https://[email protected]")]          // no project id
    public void SentryDsn_TryParse_RejectsInvalidInput(string? dsn)
    {
        var ok = SentryDsn.TryParse(dsn, out var parsed, out var err);
        ok.ShouldBeFalse();
        parsed.ShouldBeNull();
        err.ShouldNotBeNullOrWhiteSpace();
    }

    // --- TelemetryConfigurationLoader ---------------------------------------

    [Fact]
    public void Loader_LoadFromEnvironment_NoVarsSet_ReturnsSafeDefaults()
    {
        // Make sure no stray env vars are set from outside the test.
        SetEnv(TelemetryConfigurationLoader.SentryDsnEnvVar, null);
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, null);

        var options = TelemetryConfigurationLoader.LoadFromEnvironment();

        options.VerboseLogging.ShouldBeFalse();
        options.Sentry.Dsn.ShouldBeNullOrEmpty();
        options.Sentry.Environment.ShouldBe("production");
        options.Sentry.TracesSampleRate.ShouldBe(1.0);
    }

    [Fact]
    public void Loader_LoadFromEnvironment_VerboseEnvVar_Wins()
    {
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, "true");

        var options = TelemetryConfigurationLoader.LoadFromEnvironment();

        options.VerboseLogging.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("nope", false)]
    public void Loader_VerboseFlag_ParsesCommonBooleanSpellings(string raw, bool expected)
    {
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, raw);
        var options = TelemetryConfigurationLoader.LoadFromEnvironment();
        options.VerboseLogging.ShouldBe(expected);
    }

    [Fact]
    public void Loader_LoadFromConfiguration_BindsJsonShape()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:VerboseLogging"] = "true",
            ["Telemetry:Sentry:Dsn"] = "https://[email protected]/1",
            ["Telemetry:Sentry:Environment"] = "staging",
            ["Telemetry:Sentry:TracesSampleRate"] = "0.25",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        // Make sure no env var override is present that would mask the bound values.
        SetEnv(TelemetryConfigurationLoader.SentryDsnEnvVar, null);
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, null);
        SetEnv(TelemetryConfigurationLoader.SentryEnvironmentEnvVar, null);

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config);

        options.VerboseLogging.ShouldBeTrue();
        options.Sentry.Dsn.ShouldBe("https://[email protected]/1");
        options.Sentry.Environment.ShouldBe("staging");
        options.Sentry.TracesSampleRate.ShouldBe(0.25);
    }

    [Fact]
    public void Loader_EnvironmentVariables_OverrideJsonValues()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:VerboseLogging"] = "false",
            ["Telemetry:Sentry:Dsn"] = "https://[email protected]/1",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        SetEnv(TelemetryConfigurationLoader.SentryDsnEnvVar, "https://[email protected]/2");
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, "true");

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config);

        options.Sentry.Dsn.ShouldBe("https://[email protected]/2");
        options.VerboseLogging.ShouldBeTrue();
    }

    // --- AddInfrastructure --------------------------------------------------

    [Fact]
    public async Task AddInfrastructure_NoTelemetryOptions_BehavesLikeBeforeIntegration()
    {
        // The legacy single-argument overload must keep working. This is
        // the contract every existing test relies on.
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();

        await DependencyInjection.InitializeDatabaseAsync(provider);

        // Both the repository and the logger factory must be resolvable.
        provider.GetService<IGameStateRepository>().ShouldNotBeNull();
        provider.GetService<ILoggerFactory>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddInfrastructure_TelemetryOff_NoOutboundExporterErrors()
    {
        // With Sentry off, the service provider must build cleanly and
        // the breadcrumb logger must not throw. This is the "fresh
        // checkout, no Sentry account" smoke test.
        var services = new ServiceCollection();
        services.AddInfrastructure(new TelemetryOptions(), _dbPath);
        var provider = services.BuildServiceProvider();

        DependencyInjection.EmitStartupBreadcrumb(provider);
        await DependencyInjection.InitializeDatabaseAsync(provider);

        provider.GetService<TelemetryOptions>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddInfrastructure_TelemetryOnWithValidDsn_ProviderStillBuilds()
    {
        // We do not actually contact Sentry from a test — the OTLP
        // exporter buffers spans/logs in-process and flushes them on
        // a background timer. What this test verifies is that with a
        // valid DSN the container builds without throwing (no missing
        // services, no exporter-constructor crashes) and that the
        // breadcrumb fires successfully.
        var telemetry = new TelemetryOptions
        {
            VerboseLogging = false,
            Sentry =
            {
                Dsn = "https://[email protected]/2",
                Environment = "test",
                TracesSampleRate = 1.0,
            },
        };

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry, _dbPath);
        var provider = services.BuildServiceProvider();

        DependencyInjection.EmitStartupBreadcrumb(provider);
        await DependencyInjection.InitializeDatabaseAsync(provider);

        provider.GetService<IGameStateRepository>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddInfrastructure_TelemetryOnWithMalformedDsn_DoesNotCrash()
    {
        // Garbage in must not take the app down. The breadcrumb logger
        // is expected to emit a warning; we just assert no exception
        // bubbles out and the container still serves the repository.
        var telemetry = new TelemetryOptions
        {
            Sentry = { Dsn = "definitely not a url" },
        };

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry, _dbPath);
        var provider = services.BuildServiceProvider();

        Should.NotThrow(() => DependencyInjection.EmitStartupBreadcrumb(provider));
        await DependencyInjection.InitializeDatabaseAsync(provider);

        provider.GetService<IGameStateRepository>().ShouldNotBeNull();
    }

    [Fact]
    public void AddInfrastructure_VerboseFlagPropagatesToTelemetryOptionsSingleton()
    {
        // The singleton TelemetryOptions registered into the container
        // must match the values we passed in — that's the contract that
        // downstream code (ViewModels, future toggle UI) relies on to
        // know whether verbose mode is currently active.
        var telemetry = new TelemetryOptions { VerboseLogging = true };

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry, _dbPath);
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<TelemetryOptions>();
        resolved.VerboseLogging.ShouldBeTrue();
    }
}
