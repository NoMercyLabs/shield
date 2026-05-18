using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Verifies the Fix 3 contract:
//  - GET /api/push/subscriptions returns EndpointHash on every row.
//  - IsCurrentDevice is always false (server-side UA matching dropped).
//  - EndpointHash is the first 16 hex chars of SHA-256(endpoint).
public sealed class PushControllerIsCurrentDeviceTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public PushControllerIsCurrentDeviceTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSubscriptionsAlwaysReturnsIsCurrentDeviceFalse()
    {
        // Seed a subscription directly so we don't depend on the browser push stack.
        Guid userId = await SeedSubscriptionAsync(
            endpoint: "https://fcm.googleapis.com/fcm/send/test-endpoint-1",
            userAgent: "Mozilla/5.0 (Chrome/120)"
        );

        HttpClient client = _factory.CreateClient();
        // Send the *same* UA that was stored — the old code would return true here.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Chrome/120)");

        HttpResponseMessage response = await client.GetAsync("/api/push/subscriptions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        PushSubscriptionListResponse? body =
            await response.Content.ReadFromJsonAsync<PushSubscriptionListResponse>();
        body.Should().NotBeNull();

        PushSubscriptionInfo? row = body!.Subscriptions.FirstOrDefault(sub =>
            sub.UserAgent == "Mozilla/5.0 (Chrome/120)"
        );
        row.Should().NotBeNull("seeded subscription should appear in the list");
        row!
            .IsCurrentDevice.Should()
            .BeFalse("server-side UA matching was removed; IsCurrentDevice is always false");
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEndpointHashMatchingSha256Prefix()
    {
        string testEndpoint =
            "https://fcm.googleapis.com/fcm/send/hash-test-" + Guid.NewGuid().ToString("n");

        await SeedSubscriptionAsync(endpoint: testEndpoint, userAgent: null);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/push/subscriptions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        PushSubscriptionListResponse? body =
            await response.Content.ReadFromJsonAsync<PushSubscriptionListResponse>();
        body.Should().NotBeNull();

        PushSubscriptionInfo? row = body!.Subscriptions.FirstOrDefault(sub =>
            sub.Endpoint == new Uri(testEndpoint).Host
        );
        row.Should().NotBeNull("seeded subscription should appear");

        string expectedHash = ComputeExpectedHash(testEndpoint);
        row!
            .EndpointHash.Should()
            .Be(expectedHash, "EndpointHash must be SHA-256(endpoint)[0..16]");
    }

    [Fact]
    public async Task GetSubscriptionsEndpointHashIs16HexChars()
    {
        string testEndpoint =
            "https://push.services.mozilla.com/wpush/v2/test-" + Guid.NewGuid().ToString("n");

        await SeedSubscriptionAsync(endpoint: testEndpoint, userAgent: "Firefox/121");

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/push/subscriptions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        PushSubscriptionListResponse? body =
            await response.Content.ReadFromJsonAsync<PushSubscriptionListResponse>();
        body.Should().NotBeNull();

        foreach (PushSubscriptionInfo sub in body!.Subscriptions)
        {
            sub.EndpointHash.Should().HaveLength(16, "EndpointHash is always 16 hex chars");
            sub.EndpointHash.Should().MatchRegex("^[0-9a-f]{16}$", "must be lowercase hex");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<Guid> SeedSubscriptionAsync(string endpoint, string? userAgent)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        // Resolve the first admin user seeded by SingleUser mode.
        Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser> userManager =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser>>();
        Data.Identity.ShieldUser? admin = (
            await userManager.GetUsersInRoleAsync(Auth.ShieldRoles.Admin)
        ).FirstOrDefault();
        admin.Should().NotBeNull("SingleUser factory must seed an admin");

        db.PushSubscriptions.Add(
            new()
            {
                Id = Guid.NewGuid(),
                UserId = admin!.Id,
                Endpoint = endpoint,
                P256dh = "BPUBKEYPLACEHOLDER==",
                Auth = "AUTHPLACEHOLDER==",
                UserAgent = userAgent,
                CreatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static string ComputeExpectedHash(string endpoint)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
