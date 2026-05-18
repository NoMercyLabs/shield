using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Controllers;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class Fail2BanIngestTests : IClassFixture<Fail2BanIngestTests.IngestFactory>
{
    private const string IngestKey = "test-fail2ban-key-32-chars-minimum-length";
    private readonly IngestFactory _factory;

    public Fail2BanIngestTests(IngestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostWithoutSecretReturns401()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/security/fail2ban/event",
            new Fail2BanIngestRequest(
                Host: "host-a",
                Jail: "sshd",
                EventType: "ban",
                Ip: "1.2.3.4",
                At: DateTime.UtcNow,
                Matches: null
            )
        );
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostWithWrongSecretReturns401()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, "/api/security/fail2ban/event")
        {
            Content = JsonContent.Create(
                new Fail2BanIngestRequest(
                    Host: "host-a",
                    Jail: "sshd",
                    EventType: "ban",
                    Ip: "1.2.3.4",
                    At: DateTime.UtcNow,
                    Matches: null
                )
            ),
        };
        request.Headers.TryAddWithoutValidation(
            Fail2BanController.IngestKeyHeader,
            "wrong-key-but-same-length-as-real-one-yes"
        );
        HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BanEventWritesSecurityeventAndUpsertsReputation()
    {
        HttpClient client = _factory.CreateClient();
        string ip = "9.8.7." + Random.Shared.Next(1, 250);

        HttpResponseMessage response = await PostEventAsync(client, ip, "ban", "sshd");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        SecurityEvent? recorded = await db.SecurityEvents.FirstOrDefaultAsync(securityEvent =>
            securityEvent.RemoteIp == ip
        );
        recorded.Should().NotBeNull();
        recorded!.Source.Should().Be("fail2ban");
        recorded.EventType.Should().Be("fail2ban.ban");
        recorded.Severity.Should().Be(Severity.High);
        recorded.Jail.Should().Be("sshd");

        IpReputation? reputation = await db.IpReputations.FirstOrDefaultAsync(row => row.Ip == ip);
        reputation.Should().NotBeNull();
        reputation!.CurrentlyBanned.Should().BeTrue();
        reputation.LastJail.Should().Be("sshd");
        reputation.EventCount.Should().Be(1);
        reputation.Score.Should().Be(8);
    }

    [Fact]
    public async Task BanThenUnbanThenBanKeepsCurrentlyBannedInSync()
    {
        HttpClient client = _factory.CreateClient();
        string ip = "9.7.6." + Random.Shared.Next(1, 250);

        await PostEventAsync(client, ip, "ban", "nginx-noscript");
        await AssertCurrentlyBanned(ip, true, "nginx-noscript");

        await PostEventAsync(client, ip, "unban", "nginx-noscript");
        await AssertCurrentlyBanned(ip, false, "nginx-noscript");

        await PostEventAsync(client, ip, "ban", "nginx-noscript");
        await AssertCurrentlyBanned(ip, true, "nginx-noscript");

        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IpReputation reputation = (
            await db.IpReputations.FirstOrDefaultAsync(row => row.Ip == ip)
        )!;
        reputation.EventCount.Should().Be(3);
    }

    private async Task AssertCurrentlyBanned(string ip, bool expected, string expectedJail)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IpReputation? reputation = await db.IpReputations.FirstOrDefaultAsync(row => row.Ip == ip);
        reputation.Should().NotBeNull();
        reputation!.CurrentlyBanned.Should().Be(expected);
        reputation.LastJail.Should().Be(expectedJail);
    }

    private static Task<HttpResponseMessage> PostEventAsync(
        HttpClient client,
        string ip,
        string eventType,
        string jail
    )
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/api/security/fail2ban/event")
        {
            Content = JsonContent.Create(
                new Fail2BanIngestRequest(
                    Host: "test-host",
                    Jail: jail,
                    EventType: eventType,
                    Ip: ip,
                    At: DateTime.UtcNow,
                    Matches: ["log line 1", "log line 2"]
                )
            ),
        };
        request.Headers.TryAddWithoutValidation(Fail2BanController.IngestKeyHeader, IngestKey);
        return client.SendAsync(request);
    }

    public sealed class IngestFactory : ShieldWebAppFactory
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
                            ["Shield:Security:Fail2BanIngestKey"] = IngestKey,
                        }
                    );
                }
            );
        }
    }
}
