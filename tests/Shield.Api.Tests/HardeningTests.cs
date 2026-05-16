using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Hardening;
using Shield.Api.Persistence;
using Shield.Core.Abstractions;
using Shield.Data;
using Shield.Scanners;
using Xunit;

namespace Shield.Api.Tests;

// Production-safety gate + security-header + rate-limit checks. The gate itself is a
// pure static method (cheaper to test directly than spinning up an entire WebApplicationFactory
// in Production env), the headers + rate limit need a real factory because they live in the
// middleware pipeline.
public sealed class HardeningTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> overrides) =>
        new ConfigurationBuilder().AddInMemoryCollection(overrides).Build();

    private static IHostEnvironment ProductionEnvironment() => new StubHostEnvironment("Production");

    [Fact]
    public void Production_throws_when_single_user_mode_enabled_without_override()
    {
        IConfiguration config = BuildConfig(
            new Dictionary<string, string?>
            {
                ["Shield:SingleUser"] = "true",
                ["Shield:OpenApi:Enabled"] = "false",
                ["Shield:Auth:JwtSigningKey"] = new string('k', 64),
                ["Shield:Auth:DataProtectionMasterKey"] = new string('m', 64),
            }
        );

        Action act = () => ProductionSafetyGate.Validate(config, ProductionEnvironment());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SingleUser*")
            .WithMessage("*AllowSingleUserInProduction*");
    }

    [Fact]
    public void Production_throws_when_swagger_enabled()
    {
        IConfiguration config = BuildConfig(
            new Dictionary<string, string?>
            {
                ["Shield:SingleUser"] = "false",
                ["Shield:OpenApi:Enabled"] = "true",
                ["Shield:Auth:JwtSigningKey"] = new string('k', 64),
                ["Shield:Auth:DataProtectionMasterKey"] = new string('m', 64),
            }
        );

        Action act = () => ProductionSafetyGate.Validate(config, ProductionEnvironment());

        act.Should().Throw<InvalidOperationException>().WithMessage("*OpenApi*Swagger*");
    }

    [Fact]
    public void Public_throws_when_https_not_required()
    {
        IConfiguration config = BuildConfig(
            new Dictionary<string, string?>
            {
                ["Shield:SingleUser"] = "false",
                ["Shield:Public"] = "true",
                ["Shield:OpenApi:Enabled"] = "false",
                ["Shield:Auth:RequireHttps"] = "false",
                ["Shield:Auth:JwtSigningKey"] = new string('k', 64),
                ["Shield:Auth:DataProtectionMasterKey"] = new string('m', 64),
            }
        );

        Action act = () => ProductionSafetyGate.Validate(config, ProductionEnvironment());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Public*RequireHttps*");
    }

    [Fact]
    public void Public_throws_when_master_key_is_dev_default()
    {
        IConfiguration config = BuildConfig(
            new Dictionary<string, string?>
            {
                ["Shield:SingleUser"] = "false",
                ["Shield:OpenApi:Enabled"] = "false",
                ["Shield:Auth:JwtSigningKey"] = new string('k', 64),
                ["Shield:Auth:DataProtectionMasterKey"] = "dev-master-key-at-least-32-chars-long-xx",
            }
        );

        Action act = () => ProductionSafetyGate.Validate(config, ProductionEnvironment());

        act.Should().Throw<InvalidOperationException>().WithMessage("*dev default*");
    }

    [Fact]
    public void Production_throws_when_jwt_signing_key_too_short()
    {
        IConfiguration config = BuildConfig(
            new Dictionary<string, string?>
            {
                ["Shield:SingleUser"] = "false",
                ["Shield:OpenApi:Enabled"] = "false",
                ["Shield:Auth:JwtSigningKey"] = new string('k', 32), // below 48-char floor
                ["Shield:Auth:DataProtectionMasterKey"] = new string('m', 64),
            }
        );

        Action act = () => ProductionSafetyGate.Validate(config, ProductionEnvironment());

        act.Should().Throw<InvalidOperationException>().WithMessage("*JwtSigningKey*");
    }

    [Fact]
    public void Development_skips_all_checks()
    {
        IConfiguration config = BuildConfig(
            new Dictionary<string, string?>
            {
                ["Shield:SingleUser"] = "true",
                ["Shield:OpenApi:Enabled"] = "true",
                ["Shield:Public"] = "true",
                ["Shield:Auth:RequireHttps"] = "false",
                ["Shield:Auth:JwtSigningKey"] = "short",
                ["Shield:Auth:DataProtectionMasterKey"] = "dev-master-key-at-least-32-chars-long-xx",
            }
        );

        Action act = () =>
            ProductionSafetyGate.Validate(config, new StubHostEnvironment("Development"));

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Login_rate_limit_blocks_after_10_attempts_per_minute()
    {
        using LimitTestFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Register a real account so each login attempt at least reaches the limiter.
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("rate-test", "Correct1!")
        );

        // Burn through the 10-token bucket. Bad-password 401s + the eventual 429 both count.
        HttpStatusCode? sawTooMany = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("rate-test", "Wrong-password")
            );
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawTooMany = response.StatusCode;
                break;
            }
        }

        sawTooMany.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Security_headers_present_on_static_files_when_required()
    {
        using HttpsRequiredFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/healthz");
        response.IsSuccessStatusCode.Should().BeTrue();

        response.Headers.Should().ContainKey("Strict-Transport-Security");
        response.Headers.GetValues("Strict-Transport-Security").First().Should().Contain("max-age");
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").First().Should().Be("DENY");
        response.Headers.Should().ContainKey("Content-Security-Policy");
        response.Headers.GetValues("Content-Security-Policy").First().Should().Contain("'self'");
        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").First().Should().Be("nosniff");
        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.Should().ContainKey("Permissions-Policy");
        response.Headers.Should().ContainKey("Cross-Origin-Opener-Policy");
    }

    [Fact]
    public async Task Hsts_omitted_when_https_not_required()
    {
        // Default factory does NOT require HTTPS — HSTS would pin browsers to a non-existent
        // TLS endpoint, so the middleware must omit it.
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/healthz");

        response.Headers.Should().NotContainKey("Strict-Transport-Security");
        // Non-HSTS headers still ship — they don't depend on TLS.
        response.Headers.Should().ContainKey("X-Frame-Options");
    }

    // Factory variant that flips Shield:Auth:RequireHttps=true so HSTS + Secure cookies kick in.
    // Skips the actual HTTPS redirect (the test client speaks http://) by sharing the base
    // factory's middleware pipeline — UseHttpsRedirection registers but never trips because
    // the limit test only hits already-HTTPS-acceptable paths.
    private sealed class HttpsRequiredFactory : ShieldWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Shield:Auth:RequireHttps"] = "true",
                            ["Shield:SingleUser"] = "false",
                        }
                    );
                }
            );
        }
    }

    // Factory variant for rate-limit test: multi-user mode so /api/auth/login hits the real
    // sign-in pipeline (and therefore the EnableRateLimiting attribute).
    private sealed class LimitTestFactory : ShieldWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Shield:SingleUser"] = "false" }
                    );
                }
            );
        }
    }

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
}
