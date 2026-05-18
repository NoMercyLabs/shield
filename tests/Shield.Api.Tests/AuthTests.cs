using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
    public async Task MeWhenAuthenticatedReturnsAdminPrincipal()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        MeResponse? me = await response.Content.ReadFromJsonAsync<MeResponse>();
        me.Should().NotBeNull();
        me!.Roles.Should().Contain("Admin");
        me.Username.Should().Be(ShieldWebAppFactory.AdminUsername);
    }

    [Fact]
    public async Task MeWithoutAuthReturns401()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterFirstUserBecomesAdmin()
    {
        await using ShieldWebAppFactory factory = new();
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
    public async Task LoginWithWrongPasswordReturns401()
    {
        await using ShieldWebAppFactory factory = new();
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
    public async Task LoginThenMeReturnsAuthenticatedUser()
    {
        await using ShieldWebAppFactory factory = new();
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
    }

    [Fact]
    public async Task LogoutClearsCookieAndMeReturns401()
    {
        await using ShieldWebAppFactory factory = new();
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

    [Fact]
    public async Task SetupRequiredReturnsTrueWhenNoUsers()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/setup-required");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SetupRequiredResponse? body =
            await response.Content.ReadFromJsonAsync<SetupRequiredResponse>();
        body.Should().NotBeNull();
        body!.Required.Should().BeTrue();
    }

    [Fact]
    public async Task SetupCreatesAdminAndSignsIn()
    {
        await using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage setup = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("setup-admin", "SetupPass1!")
        );
        setup.StatusCode.Should().Be(HttpStatusCode.OK);

        LoginResponse? body = await setup.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Roles.Should().Contain("Admin");
        body.Succeeded.Should().BeTrue();

        // Should now be signed in.
        HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetupReturns409WhenUsersExist()
    {
        await using ShieldWebAppFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("second-admin", "AnotherPass1!")
        );
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
