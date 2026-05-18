using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Shield.Api.Tests;

// ForwardedHeaders middleware decides whether to honour X-Forwarded-For based on whether the
// immediate hop is in KnownProxies / KnownNetworks. Two angles here:
//   1. Direct config inspection — KnownProxies must include loopback even when the operator
//      adds nothing, ForwardLimit must be set explicitly (defaults are framework-dependent).
//   2. Behavioural — a /healthz hit through TestServer carries no real connection IP, so we
//      exercise the option shape rather than a live ::1 vs spoof comparison.
public sealed class ForwardedHeadersTests
{
    [Fact]
    public void Loopback_is_always_in_known_proxies_even_without_config()
    {
        using ShieldWebAppFactory factory = new();
        // Force the host to materialise so Configure<ForwardedHeadersOptions> runs.
        _ = factory.CreateClient();

        IOptions<ForwardedHeadersOptions> options = factory.Services.GetRequiredService<
            IOptions<ForwardedHeadersOptions>
        >();

        options.Value.KnownProxies.Should().Contain(IPAddress.Loopback);
        options.Value.KnownProxies.Should().Contain(IPAddress.IPv6Loopback);
    }

    [Fact]
    public void Forward_limit_is_capped_at_configured_value()
    {
        using ShieldWebAppFactory factory = new();
        _ = factory.CreateClient();

        IOptions<ForwardedHeadersOptions> options = factory.Services.GetRequiredService<
            IOptions<ForwardedHeadersOptions>
        >();

        // Default in our config is 2 — anything higher and an untrusted client could pollute
        // the IP chain by stacking spoofed hops, anything lower breaks the cloudflared->Shield
        // single hop for ops that legitimately need scheme rewrite.
        options.Value.ForwardLimit.Should().Be(2);
    }

    [Fact]
    public void Configured_known_proxies_are_added_alongside_loopback()
    {
        using KnownProxyFactory factory = new();
        _ = factory.CreateClient();

        IOptions<ForwardedHeadersOptions> options = factory.Services.GetRequiredService<
            IOptions<ForwardedHeadersOptions>
        >();

        options.Value.KnownProxies.Should().Contain(IPAddress.Parse("10.0.0.5"));
        options.Value.KnownProxies.Should().Contain(IPAddress.Parse("10.0.0.6"));
        // Loopback still survives — operator config is additive, never subtractive.
        options.Value.KnownProxies.Should().Contain(IPAddress.Loopback);
    }

    [Fact]
    public void Configured_known_networks_are_parsed_from_cidr_csv()
    {
        using KnownNetworkFactory factory = new();
        _ = factory.CreateClient();

        IOptions<ForwardedHeadersOptions> options = factory.Services.GetRequiredService<
            IOptions<ForwardedHeadersOptions>
        >();

        options.Value.KnownIPNetworks.Should().HaveCount(2);
        options
            .Value.KnownIPNetworks.Should()
            .Contain(network =>
                network.BaseAddress.Equals(IPAddress.Parse("10.0.0.0")) && network.PrefixLength == 24
            );
        // System.Net.IPNetwork canonicalises the base address: bits beyond PrefixLength are
        // zeroed, so 192.168.1.0/16 → 192.168.0.0/16.
        options
            .Value.KnownIPNetworks.Should()
            .Contain(network =>
                network.BaseAddress.Equals(IPAddress.Parse("192.168.0.0")) && network.PrefixLength == 16
            );
    }

    [Fact]
    public async Task Healthz_round_trip_is_unaffected_by_spoofed_xff_header()
    {
        // Behavioural smoke: TestServer's connection IP is null in-process. The middleware
        // must STILL accept the loopback shortcut (it has no immediate hop to evaluate) and
        // return 200 — proving the configuration didn't accidentally hard-fail every request.
        // The audit-log spoof-rejection assertion lives in the wire-level smoke test in
        // docs/auth.md because TestServer can't synthesise a real remote IP for the
        // not-a-known-proxy case.
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/healthz");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "1.2.3.4");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class KnownProxyFactory : ShieldWebAppFactory
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
                            ["Shield:ForwardedHeaders:KnownProxies"] = "10.0.0.5, 10.0.0.6",
                        }
                    );
                }
            );
        }
    }

    private sealed class KnownNetworkFactory : ShieldWebAppFactory
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
                            ["Shield:ForwardedHeaders:KnownNetworks"] =
                                "10.0.0.0/24, 192.168.1.0/16",
                        }
                    );
                }
            );
        }
    }
}
