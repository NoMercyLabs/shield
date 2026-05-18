using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SecurityEventsTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public SecurityEventsTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Events_filter_by_severity_returns_only_matching_rows()
    {
        await SeedEventAsync("shield.auth", "login.failed", Severity.Medium, ip: "10.0.0.1");
        await SeedEventAsync("shield.auth", "login.lockout", Severity.High, ip: "10.0.0.2");
        await SeedEventAsync("shield.ratelimit", "rate.limit", Severity.Low, ip: "10.0.0.3");

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/security/events?minSeverity=2&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SecurityEventsPage? page = await response.Content.ReadFromJsonAsync<SecurityEventsPage>();
        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().OnlyContain(item => item.Severity >= Severity.High);
    }

    [Fact]
    public async Task Events_filter_by_source_returns_only_matching_rows()
    {
        await SeedEventAsync("shield.auth", "login.failed", Severity.Medium, ip: "10.0.1.1");
        await SeedEventAsync("shield.crawler", "crawler.detected", Severity.Low, ip: "10.0.1.2");

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/security/events?source=shield.crawler&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SecurityEventsPage? page = await response.Content.ReadFromJsonAsync<SecurityEventsPage>();
        page!.Items.Should().OnlyContain(item => item.Source == "shield.crawler");
    }

    [Fact]
    public async Task Events_filter_by_ip_returns_only_matching_rows()
    {
        string ip = "10.99.0." + Random.Shared.Next(1, 250);
        await SeedEventAsync("shield.auth", "login.failed", Severity.Medium, ip: ip);
        await SeedEventAsync("shield.auth", "login.failed", Severity.Medium, ip: "10.99.0.99");

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/security/events?ip={ip}&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SecurityEventsPage? page = await response.Content.ReadFromJsonAsync<SecurityEventsPage>();
        page!.Items.Should().OnlyContain(item => item.RemoteIp == ip);
    }

    [Fact]
    public async Task Hosts_returns_distinct_hosts_with_counts()
    {
        await SeedEventAsync(
            "fail2ban",
            "fail2ban.ban",
            Severity.High,
            ip: "172.16.0.1",
            host: "host-a"
        );
        await SeedEventAsync(
            "fail2ban",
            "fail2ban.ban",
            Severity.High,
            ip: "172.16.0.2",
            host: "host-a"
        );
        await SeedEventAsync(
            "fail2ban",
            "fail2ban.ban",
            Severity.High,
            ip: "172.16.0.3",
            host: "host-b"
        );

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/security/hosts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HostsResponse? hosts = await response.Content.ReadFromJsonAsync<HostsResponse>();
        hosts!.Items.Should().Contain(host => host.Host == "host-a" && host.EventCount >= 2);
        hosts.Items.Should().Contain(host => host.Host == "host-b" && host.EventCount >= 1);
    }

    private async Task SeedEventAsync(
        string source,
        string eventType,
        Severity severity,
        string ip,
        string? host = null
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ISecurityEventLogger logger =
            scope.ServiceProvider.GetRequiredService<ISecurityEventLogger>();
        await logger.LogAsync(
            source: source,
            eventType: eventType,
            severity: severity,
            remoteIp: ip,
            host: host
        );
    }
}
