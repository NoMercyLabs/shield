using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shield.Api.Hardening;
using Xunit;

namespace Shield.Api.Tests;

// Covers every refused-boot path in ProductionSafetyGate that isn't already exercised
// by HardeningTests.  Pure-static-method tests are cheaper than spinning up a full
// WebApplicationFactory, so most assertions here call Validate() directly against a
// StubHostEnvironment("Production") + in-memory IConfiguration.
//
// The banner test is the exception: LogPostureBanner is called via the real startup path
// so it needs a live factory in Development (the only env that skips the gate entirely).
public sealed class ProductionSafetyGateTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Production() => new StubHostEnvironment("Production");

    // Full "green" config — everything set to safe values so individual tests can flip
    // exactly one knob and confirm it fires.
    private static Dictionary<string, string?> GreenConfig() =>
        new()
        {
            ["Shield:OpenApi:Enabled"] = "false",
            ["Shield:Public"] = "false",
            ["Shield:Auth:RequireHttps"] = "false",
            ["Shield:Auth:JwtSigningKey"] = new('k', 64),
            ["Shield:Auth:DataProtectionMasterKey"] = new('m', 64),
            ["Shield:Auth:ApiTokenPepper"] = new('p', 48),
        };

    // ---------------------------------------------------------------------------
    // 1.  No DataProtectionMasterKey in non-Development env
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesWhenMasterKeyAbsent()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:DataProtectionMasterKey"] = string.Empty;

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*DataProtectionMasterKey*");
    }

    [Fact]
    public void RefusesWhenMasterKeyShorterThan32Chars()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:DataProtectionMasterKey"] = new('m', 16);

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*DataProtectionMasterKey*");
    }

    // ---------------------------------------------------------------------------
    // 2.  JwtSigningKey shorter than 32 characters (gate floor is 48)
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesWhenJwtKeyShorterThan48Chars()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:JwtSigningKey"] = new('k', 47);

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*JwtSigningKey*48*");
    }

    [Fact]
    public void AllowsJwtKeyAtExactly48Chars()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:JwtSigningKey"] = new('k', 48);

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // 3.  Public=true AND RequireHttps=false
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesPublicWithoutHttps()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Public"] = "true";
        config["Shield:Auth:RequireHttps"] = "false";
        config["Shield:Auth:CookieDomain"] = "shield.example.com";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Public*RequireHttps*");
    }

    // ---------------------------------------------------------------------------
    // 5.  Public=true AND no CookieDomain
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesPublicWithoutCookieDomain()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Public"] = "true";
        config["Shield:Auth:RequireHttps"] = "true";
        // CookieDomain intentionally absent

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*CookieDomain*");
    }

    [Fact]
    public void AllowsPublicWithHttpsAndCookieDomain()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Public"] = "true";
        config["Shield:Auth:RequireHttps"] = "true";
        config["Shield:Auth:CookieDomain"] = "shield.example.com";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // 6.  OAuthRedirectBase http:// while RequireHttps=true
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesOauthRedirectBaseHttpWhenHttpsRequired()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:RequireHttps"] = "true";
        config["Shield:Auth:OAuthRedirectBase"] = "http://shield.example.com";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*OAuthRedirectBase*http://*");
    }

    [Fact]
    public void AllowsOauthRedirectBaseHttpsWhenHttpsRequired()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:RequireHttps"] = "true";
        config["Shield:Auth:OAuthRedirectBase"] = "https://shield.example.com";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().NotThrow();
    }

    [Fact]
    public void AllowsOauthRedirectBaseHttpWhenHttpsNotRequired()
    {
        // Internal LAN deploy — https not required, http redirect base is fine.
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:RequireHttps"] = "false";
        config["Shield:Auth:OAuthRedirectBase"] = "http://shield.lan";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().NotThrow();
    }

    [Fact]
    public void AllowsAbsentOauthRedirectBaseWhenHttpsRequired()
    {
        // No OAuthRedirectBase configured — falls back to request-derived at runtime.
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:RequireHttps"] = "true";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // 5b. ApiTokenPepper absent in Production
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesWhenApiTokenPepperAbsent()
    {
        Dictionary<string, string?> config = GreenConfig();
        config.Remove("Shield:Auth:ApiTokenPepper");

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiTokenPepper*");
    }

    // ---------------------------------------------------------------------------
    // 7.  Dev default master key is rejected
    // ---------------------------------------------------------------------------

    [Fact]
    public void RefusesDevDefaultMasterKey()
    {
        Dictionary<string, string?> config = GreenConfig();
        config["Shield:Auth:DataProtectionMasterKey"] = "dev-master-key-at-least-32-chars-long-xx";

        Action act = () => ProductionSafetyGate.Validate(BuildConfig(config), Production());

        act.Should().Throw<InvalidOperationException>().WithMessage("*dev default*");
    }

    // ---------------------------------------------------------------------------
    // 8.  Testing environment skips the gate (same as Development)
    // ---------------------------------------------------------------------------

    [Fact]
    public void TestingEnvironmentSkipsAllChecks()
    {
        IConfiguration badConfig = BuildConfig(
            new()
            {
                ["Shield:OpenApi:Enabled"] = "true",
                ["Shield:Public"] = "true",
                ["Shield:Auth:RequireHttps"] = "false",
                ["Shield:Auth:JwtSigningKey"] = "short",
                ["Shield:Auth:DataProtectionMasterKey"] =
                    "dev-master-key-at-least-32-chars-long-xx",
            }
        );

        Action act = () =>
            ProductionSafetyGate.Validate(badConfig, new StubHostEnvironment("Testing"));

        act.Should().NotThrow("Testing environment bypasses all gate checks");
    }

    // ---------------------------------------------------------------------------
    // 9.  Multiple failures aggregated into one exception
    // ---------------------------------------------------------------------------

    [Fact]
    public void MultipleFailuresAreAggregatedIntoOneException()
    {
        IConfiguration config = BuildConfig(
            new()
            {
                ["Shield:OpenApi:Enabled"] = "true",
                ["Shield:Auth:JwtSigningKey"] = new('k', 32),
                ["Shield:Auth:DataProtectionMasterKey"] = new('m', 64),
            }
        );

        Action act = () => ProductionSafetyGate.Validate(config, Production());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*OpenApi*")
            .WithMessage("*JwtSigningKey*");
    }

    // ---------------------------------------------------------------------------
    // 10.  Posture banner is emitted at boot in Development
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostureBannerIsLoggedAtBoot()
    {
        // Use a fresh factory so the banner logger fires in a controlled scope.
        // The factory runs in Testing environment, which skips the gate entirely
        // and still calls LogPostureBanner.
        await using PostureBannerFactory factory = new();
        _ = factory.CreateClient(); // trigger startup

        // Give the hosted services a moment to write the log.
        await Task.Delay(200);

        factory
            .LoggedBanner.Should()
            .BeTrue("LogPostureBanner must write the posture log line at startup");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Shield.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // Factory variant that captures whether the "Shield posture:" log line was emitted.
    private sealed class PostureBannerFactory : ShieldWebAppFactory
    {
        public bool LoggedBanner { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddLogging(logging =>
                {
                    logging.AddProvider(new BannerCaptureProvider(this));
                });
            });
        }

        internal void RecordBanner() => LoggedBanner = true;

        private sealed class BannerCaptureProvider : ILoggerProvider
        {
            private readonly PostureBannerFactory _factory;

            public BannerCaptureProvider(PostureBannerFactory factory) => _factory = factory;

            public ILogger CreateLogger(string categoryName) =>
                new BannerCaptureLogger(_factory, categoryName);

            public void Dispose() { }
        }

        private sealed class BannerCaptureLogger : ILogger
        {
            private readonly PostureBannerFactory _factory;
            private readonly string _category;

            public BannerCaptureLogger(PostureBannerFactory factory, string category)
            {
                _factory = factory;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                string message = formatter(state, exception);
                if (message.Contains("Shield posture:"))
                    _factory.RecordBanner();
            }
        }
    }
}
