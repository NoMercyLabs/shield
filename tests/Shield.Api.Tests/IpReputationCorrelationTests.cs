using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services.Security;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class IpReputationCorrelationTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public IpReputationCorrelationTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MultipleInternalEventsForSameIpAccumulateScoreAndCount()
    {
        string ip = "203.0.113." + Random.Shared.Next(1, 250);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ISecurityEventLogger logger =
                scope.ServiceProvider.GetRequiredService<ISecurityEventLogger>();
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.failed",
                severity: Severity.Medium,
                remoteIp: ip
            );
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.failed",
                severity: Severity.Medium,
                remoteIp: ip
            );
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.lockout",
                severity: Severity.High,
                remoteIp: ip
            );
        }

        using IServiceScope readScope = _factory.Services.CreateScope();
        ShieldDbContext db = readScope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IpReputation? reputation = await db.IpReputations.FirstOrDefaultAsync(row => row.Ip == ip);
        reputation.Should().NotBeNull();
        reputation!.EventCount.Should().Be(3);
        // Medium=3, Medium=3, High=8 → 14
        reputation.Score.Should().Be(14);
    }

    [Fact]
    public async Task IpAppearsInReputationViewViaApi()
    {
        string ip = "198.51.100." + Random.Shared.Next(1, 250);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ISecurityEventLogger logger =
                scope.ServiceProvider.GetRequiredService<ISecurityEventLogger>();
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.failed",
                severity: Severity.Medium,
                remoteIp: ip
            );
        }

        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/security/ips?search={ip}&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IpReputationsPage? page = await response.Content.ReadFromJsonAsync<IpReputationsPage>();
        page!.Items.Should().Contain(item => item.Ip == ip);
    }

    [Fact]
    public async Task IpDetailReturnsReputationWithRecentEvents()
    {
        string ip = "192.0.2." + Random.Shared.Next(1, 250);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ISecurityEventLogger logger =
                scope.ServiceProvider.GetRequiredService<ISecurityEventLogger>();
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.failed",
                severity: Severity.Medium,
                remoteIp: ip
            );
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.failed",
                severity: Severity.Medium,
                remoteIp: ip
            );
        }

        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.GetAsync($"/api/security/ips/{ip}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IpDetailResponseContract? detail =
            await response.Content.ReadFromJsonAsync<IpDetailResponseContract>();
        detail.Should().NotBeNull();
        detail!.Reputation.Ip.Should().Be(ip);
        detail.RecentEvents.Should().HaveCount(2);
    }

    [Fact]
    public async Task NotesCanBeAttachedToAnIp()
    {
        string ip = "172.16.99." + Random.Shared.Next(1, 250);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ISecurityEventLogger logger =
                scope.ServiceProvider.GetRequiredService<ISecurityEventLogger>();
            await logger.LogAsync(
                source: "shield.auth",
                eventType: "login.failed",
                severity: Severity.Medium,
                remoteIp: ip
            );
        }

        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/security/ips/{ip}/notes",
            new UpdateNotesRequest("known scanner from VPS pool")
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IpReputationResponse? updated =
            await response.Content.ReadFromJsonAsync<IpReputationResponse>();
        updated!.Notes.Should().Be("known scanner from VPS pool");
    }

    // Re-declares the IpDetailResponse shape on the test side so we don't depend on the
    // controller's nested record being accessible. Properties match.
    private sealed record IpDetailResponseContract(
        IpReputationResponse Reputation,
        IReadOnlyList<SecurityEventResponse> RecentEvents
    );
}
