using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Data.Identity;
using Xunit;

namespace Shield.Api.Tests;

public sealed class ImpersonationTests
{
    [Fact]
    public async Task StartAsAdminSucceedsAndMeReturnsImpersonatedIdentity()
    {
        using MultiUserImpersonationFactory factory = new();
        Guid viewerId = await SeedAdminAndViewerAsync(factory);

        HttpClient admin = factory.CreateClient();
        HttpResponseMessage login = await admin.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("imp-admin", "Admin1Pass!")
        );
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage start = await admin.PostAsJsonAsync(
            "/api/impersonation/start",
            new { userId = viewerId.ToString() }
        );
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        ImpersonationStartResponse? body =
            await start.Content.ReadFromJsonAsync<ImpersonationStartResponse>();
        body.Should().NotBeNull();
        body!.Username.Should().Be("imp-viewer");

        HttpResponseMessage me = await admin.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResponse? meBody = await me.Content.ReadFromJsonAsync<MeResponse>();
        meBody!.Username.Should().Be("imp-viewer");
        meBody.Roles.Should().NotContain("Admin");
        meBody.ImpersonatedBy.Should().NotBeNullOrEmpty();
        meBody.ImpersonatorLogin.Should().Be("imp-admin");
    }

    [Fact]
    public async Task StartAsNonAdminReturns403()
    {
        using MultiUserImpersonationFactory factory = new();
        Guid viewerId = await SeedAdminAndViewerAsync(factory);

        HttpClient viewer = factory.CreateClient();
        await viewer.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("imp-viewer", "Viewer1Pass!")
        );

        HttpResponseMessage start = await viewer.PostAsJsonAsync(
            "/api/impersonation/start",
            new { userId = viewerId.ToString() }
        );
        start.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MutatingEndpointDuringImpersonationReturns403ImpersonationBlocked()
    {
        using MultiUserImpersonationFactory factory = new();
        Guid viewerId = await SeedAdminAndViewerAsync(factory);

        HttpClient admin = factory.CreateClient();
        await admin.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("imp-admin", "Admin1Pass!")
        );
        HttpResponseMessage start = await admin.PostAsJsonAsync(
            "/api/impersonation/start",
            new { userId = viewerId.ToString() }
        );
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        // While impersonating, the admin tries to invite a new collaborator. The endpoint
        // is gated by RequireOriginalIdentity, so even though the swapped principal is a
        // Viewer (which would already be blocked by the Admin policy), the attribute
        // returns the specific `impersonation_blocked` payload first.
        HttpResponseMessage invite = await admin.PostAsJsonAsync(
            "/api/access/invite",
            new { email = "new@example.com", role = "Viewer" }
        );
        invite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        string raw = await invite.Content.ReadAsStringAsync();
        raw.Should().Contain("impersonation_blocked");
    }

    [Fact]
    public async Task StopRestoresAdminIdentity()
    {
        using MultiUserImpersonationFactory factory = new();
        Guid viewerId = await SeedAdminAndViewerAsync(factory);

        HttpClient admin = factory.CreateClient();
        await admin.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("imp-admin", "Admin1Pass!")
        );
        await admin.PostAsJsonAsync(
            "/api/impersonation/start",
            new { userId = viewerId.ToString() }
        );

        HttpResponseMessage stop = await admin.PostAsync("/api/impersonation/stop", content: null);
        stop.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage me = await admin.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResponse? meBody = await me.Content.ReadFromJsonAsync<MeResponse>();
        meBody!.Username.Should().Be("imp-admin");
        meBody.Roles.Should().Contain("Admin");
        meBody.ImpersonatedBy.Should().BeNull();
    }

    // ---------- helpers ----------

    private static async Task<Guid> SeedAdminAndViewerAsync(MultiUserImpersonationFactory factory)
    {
        HttpClient bootstrap = factory.CreateClient();
        // First registration becomes Admin automatically.
        await bootstrap.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("imp-admin", "Admin1Pass!")
        );

        Guid viewerId;
        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ShieldUser viewer = new()
        {
            UserName = "imp-viewer",
            Email = "viewer@test",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };
        await userManager.CreateAsync(viewer, "Viewer1Pass!");
        await userManager.AddToRoleAsync(viewer, "Viewer");
        viewerId = viewer.Id;
        return viewerId;
    }

    private sealed class MultiUserImpersonationFactory : ShieldWebAppFactory
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
}
