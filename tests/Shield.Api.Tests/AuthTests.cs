using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shield.Api.Contracts;
using Xunit;

namespace Shield.Api.Tests;

public sealed class AuthTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public AuthTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Me_in_single_user_mode_returns_admin_principal()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        MeResponse? me = await response.Content.ReadFromJsonAsync<MeResponse>();
        me.Should().NotBeNull();
        me!.SingleUserMode.Should().BeTrue();
        me.Roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task Register_first_user_becomes_admin()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();

        RegisterRequest request = new("first-admin", "P@ssword1");
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        RegisterResponse? body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        body.Should().NotBeNull();
        body!.Username.Should().Be("first-admin");
        body.Roles.Should().Contain("Admin");

        // Second registration should default to Viewer.
        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("second-viewer", "P@ssword1")
        );
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        RegisterResponse? secondBody = await second.Content.ReadFromJsonAsync<RegisterResponse>();
        secondBody!.Roles.Should().Contain("Viewer").And.NotContain("Admin");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("user-a", "Correct1!")
        );

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("user-a", "Wrong-password")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_then_me_returns_authenticated_user()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("logged-in", "Correct1!")
        );

        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("logged-in", "Correct1!")
        );
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.Should().ContainKey("Set-Cookie");

        HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResponse? body = await me.Content.ReadFromJsonAsync<MeResponse>();
        body!.Username.Should().Be("logged-in");
        body.SingleUserMode.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_clears_cookie_and_me_returns_401()
    {
        using MultiUserFactory factory = new();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("logout-user", "Correct1!")
        );
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("logout-user", "Correct1!")
        );

        HttpResponseMessage logout = await client.PostAsync("/api/auth/logout", content: null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Multi-user mode factory — overrides Shield:SingleUser=false on top of the shared factory
    // so tests that exercise the real login pipeline don't get the synthetic shortcut.
    private sealed class MultiUserFactory : ShieldWebAppFactory
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
