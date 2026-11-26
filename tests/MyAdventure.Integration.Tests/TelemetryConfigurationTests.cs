using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAdventure.Core.Interfaces;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Data;
using MyAdventure.Infrastructure.Telemetry;
using Shouldly;

namespace MyAdventure.Integration.Tests;

public class TelemetryConfigurationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly List<string> _envVarsToRestore = new();

    public TelemetryConfigurationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"myadventure-test-{Guid.NewGuid():N}.db");

        SetEnv(TelemetryConfigurationLoader.SentryDsnEnvVar, null);
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, null);
        SetEnv(TelemetryConfigurationLoader.SentryEnvironmentEnvVar, null);
    }

    public void Dispose()
    {
        foreach (var name in _envVarsToRestore)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private static IConfiguration EmptyConfiguration => new ConfigurationBuilder().Build();

    private void SetEnv(string name, string? value)
    {
        if (!_envVarsToRestore.Contains(name)) _envVarsToRestore.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

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
    public void SentryDsn_AuthHeader_MatchesSentryDocumentedFormat()
    {
        const string dsn =
            "https://abc123@o123.ingest.us.sentry.io/456";

        var parsed = SentryDsn.Parse(dsn);

        parsed.AuthHeaderValue.ShouldBe("sentry sentry_key=abc123");
    }

    [Fact]
    public void SentryDsn_AuthHeader_ContainsNoComma_RegressionForOtlpHeaderSplit()
    {
        const string modernDsn =
            "https://abc123@o123.ingest.us.sentry.io/456";
        const string legacyDsn =
            "https://abc123:secret456@o123.ingest.us.sentry.io/789";

        SentryDsn.Parse(modernDsn).AuthHeaderValue.ShouldNotContain(",");
        SentryDsn.Parse(legacyDsn).AuthHeaderValue.ShouldNotContain(",");
    }

    [Fact]
    public void SentryDsn_AuthHeader_LegacySecretKeyIncluded()
    {
        const string dsn = "https://pubkey:secret@example.com/9";

        var parsed = SentryDsn.Parse(dsn);

        parsed.AuthHeaderValue.ShouldBe(
            "sentry sentry_key=pubkey sentry_secret=secret");
    }

    [Fact]
    public void SentryDsn_TryParse_HandlesLegacyPublicSecretKeyFormat()
    {
        const string dsn = "https://pubkey:potato@example.com/9";
        var parsed = SentryDsn.Parse(dsn);
        parsed.PublicKey.ShouldBe("pubkey");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-url")]
    [InlineData("ftp://x@example.com/1")]
    [InlineData("https://example.com/1")]
    [InlineData("https://potato@example.com")]
    public void SentryDsn_TryParse_RejectsInvalidInput(string? dsn)
    {
        var ok = SentryDsn.TryParse(dsn, out var parsed, out var err);
        ok.ShouldBeFalse();
        parsed.ShouldBeNull();
        err.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Loader_NoFallback_NoVarsSet_ReturnsSafeDefaults()
    {
        var options = TelemetryConfigurationLoader.LoadFromConfiguration(EmptyConfiguration, fallbackDsn: "");

        options.VerboseLogging.ShouldBeFalse();
        options.Sentry.Dsn.ShouldBeNullOrEmpty();
        options.Sentry.Environment.ShouldBe("production");
        options.Sentry.TracesSampleRate.ShouldBe(1.0);
    }

    [Fact]
    public void Loader_NoFallback_VerboseEnvVar_Wins()
    {
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, "true");

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(EmptyConfiguration, fallbackDsn: "");

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
        var options = TelemetryConfigurationLoader.LoadFromConfiguration(EmptyConfiguration, fallbackDsn: "");
        options.VerboseLogging.ShouldBe(expected);
    }

    [Fact]
    public void Loader_NoFallback_BindsJsonShape()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:VerboseLogging"] = "true",
            ["Telemetry:Sentry:Dsn"] = "https://potato@example.com/1",
            ["Telemetry:Sentry:Environment"] = "staging",
            ["Telemetry:Sentry:TracesSampleRate"] = "0.25",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config, fallbackDsn: "");

        options.VerboseLogging.ShouldBeTrue();
        options.Sentry.Dsn.ShouldBe("https://potato@example.com/1");
        options.Sentry.Environment.ShouldBe("staging");
        options.Sentry.TracesSampleRate.ShouldBe(0.25);
    }

    [Fact]
    public void Loader_EnvironmentVariables_OverrideJsonValues()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:VerboseLogging"] = "false",
            ["Telemetry:Sentry:Dsn"] = "https://potato@example.com/1",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        SetEnv(TelemetryConfigurationLoader.SentryDsnEnvVar, "https://potato@example.com/2");
        SetEnv(TelemetryConfigurationLoader.VerboseLoggingEnvVar, "true");

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config, fallbackDsn: "");

        options.Sentry.Dsn.ShouldBe("https://potato@example.com/2");
        options.VerboseLogging.ShouldBeTrue();
    }

    private const string TestFallback = "https://potato@example.com/9";

    [Fact]
    public void Loader_WithFallback_NoVarsSet_UsesFallbackDsn()
    {
        var options = TelemetryConfigurationLoader.LoadFromConfiguration(EmptyConfiguration, TestFallback);

        options.Sentry.Dsn.ShouldBe(TestFallback);
    }

    [Fact]
    public void Loader_WithFallback_AppsettingsEmptyDsn_UsesFallback()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:Sentry:Dsn"] = "",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config, TestFallback);

        options.Sentry.Dsn.ShouldBe(TestFallback);
    }

    [Fact]
    public void Loader_WithFallback_AppsettingsNonEmptyDsn_OverridesFallback()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:Sentry:Dsn"] = "https://potato@example.com/1",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config, TestFallback);

        options.Sentry.Dsn.ShouldBe("https://potato@example.com/1");
    }

    [Fact]
    public void Loader_WithFallback_EnvVar_OverridesEverything()
    {
        var json = new Dictionary<string, string?>
        {
            ["Telemetry:Sentry:Dsn"] = "https://potato@example.com/1",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(json).Build();

        SetEnv(TelemetryConfigurationLoader.SentryDsnEnvVar, "https://potato@example.com/2");

        var options = TelemetryConfigurationLoader.LoadFromConfiguration(config, TestFallback);

        options.Sentry.Dsn.ShouldBe("https://potato@example.com/2");
    }

    [Fact]
    public void TelemetryDefaults_DefaultDsn_IsAValidParseableSentryDsn()
    {
        if (string.IsNullOrWhiteSpace(TelemetryDefaults.DefaultDsn))
        {
            return;
        }

        var ok = SentryDsn.TryParse(TelemetryDefaults.DefaultDsn, out var parsed, out var err);

        ok.ShouldBeTrue(err);
        parsed.ShouldNotBeNull();
        parsed.PublicKey.ShouldNotBeNullOrWhiteSpace();
        parsed.ProjectId.ShouldNotBeNullOrWhiteSpace();
        parsed.Host.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AddInfrastructure_NoTelemetryOptions_BehavesLikeBeforeIntegration()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_dbPath);
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        provider.GetService<IGameStateRepository>().ShouldNotBeNull();
        provider.GetService<ILoggerFactory>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddInfrastructure_TelemetryOff_NoOutboundExporterErrors()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new TelemetryOptions(), _dbPath);
        var provider = services.BuildServiceProvider();

        TelemetryLifecycle.Start(provider);
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        provider.GetService<TelemetryOptions>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddInfrastructure_TelemetryOnWithValidDsn_ProviderStillBuilds()
    {
        var telemetry = new TelemetryOptions
        {
            VerboseLogging = false,
            Sentry =
            {
                Dsn = "https://potato@example.com/2",
                Environment = "test",
                TracesSampleRate = 1.0,
            },
        };

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry, _dbPath);
        var provider = services.BuildServiceProvider();

        TelemetryLifecycle.Start(provider);
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        provider.GetService<IGameStateRepository>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddInfrastructure_TelemetryOnWithMalformedDsn_DoesNotCrash()
    {
        var telemetry = new TelemetryOptions
        {
            Sentry = { Dsn = "definitely not a url" },
        };

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry, _dbPath);
        var provider = services.BuildServiceProvider();

        Should.NotThrow(() => TelemetryLifecycle.Start(provider));
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        provider.GetService<IGameStateRepository>().ShouldNotBeNull();
    }

    [Fact]
    public void AddInfrastructure_VerboseFlagPropagatesToTelemetryOptionsSingleton()
    {
        var telemetry = new TelemetryOptions { VerboseLogging = true };

        var services = new ServiceCollection();
        services.AddInfrastructure(telemetry, _dbPath);
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<TelemetryOptions>();
        resolved.VerboseLogging.ShouldBeTrue();
    }
}
