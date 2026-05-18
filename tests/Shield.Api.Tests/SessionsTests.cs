using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SessionsTests
{
    [Fact]
    public async Task LoginCreatesSessionRow()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();
        await RegisterAndLoginAsync(client, "sess-login", "Correct1!");

        using IServiceScope scope = factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        int count = await db.UserSessions.CountAsync(session => session.RevokedAt == null);
        count.Should().Be(1);
    }

    [Fact]
    public async Task RevokeOtherSessionsNukesAllButCurrent()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient currentClient = factory.CreateClient();
        await RegisterAndLoginAsync(currentClient, "sess-many", "Correct1!");

        // Two extra sessions (cookies live on independent HttpClient handlers).
        HttpClient other1 = factory.CreateClient();
        HttpClient other2 = factory.CreateClient();
        (
            await other1.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("sess-many", "Correct1!")
            )
        )
            .IsSuccessStatusCode.Should()
            .BeTrue();
        (
            await other2.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("sess-many", "Correct1!")
            )
        )
            .IsSuccessStatusCode.Should()
            .BeTrue();

        HttpResponseMessage revoke = await currentClient.PostAsync(
            "/api/sessions/revoke-others",
            content: null
        );
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        RevokeOthersResponse? body = await revoke.Content.ReadFromJsonAsync<RevokeOthersResponse>();
        body!.Revoked.Should().Be(2);

        SessionListResponse? listing = await currentClient.GetFromJsonAsync<SessionListResponse>(
            "/api/sessions"
        );
        listing!.Sessions.Should().HaveCount(1);
        listing.Sessions[0].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task RevokedCookieReturns401NextRequest()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();
        await RegisterAndLoginAsync(client, "sess-revoke", "Correct1!");

        // Find the only session row and forcibly revoke it server-side.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Core.Domain.UserSession session = (await db.UserSessions.FirstAsync())!;
            session.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task RegisterAndLoginAsync(
        HttpClient client,
        string username,
        string password
    )
    {
        HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(username, password)
        );
        register.IsSuccessStatusCode.Should().BeTrue();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, password)
        );
        login.IsSuccessStatusCode.Should().BeTrue();
    }
}
