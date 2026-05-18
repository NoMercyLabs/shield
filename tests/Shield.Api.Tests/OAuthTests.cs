using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Auth.OAuthProviders;
using Shield.Api.Contracts;
using Shield.Api.Services.Auth;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

public sealed class OAuthTests
{
    [Fact]
    public async Task StatusReturnsDisconnectedWhenNothingPersisted()
    {
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/oauth/github/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        OAuthStatusResponse? body = await response.Content.ReadFromJsonAsync<OAuthStatusResponse>();
        body.Should().NotBeNull();
        body!.Connected.Should().BeFalse();
        body.Provider.Should().Be(OAuthProvider.Github);
    }

    [Fact]
    public async Task StatusReturnsConnectedWhenTokenSaved()
    {
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "ghs_test_token_value",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user public_repo",
                AccountLogin: "octocat",
                AccountId: "1",
                Extra: null
            )
        );

        OAuthStatusResponse? body = await client.GetFromJsonAsync<OAuthStatusResponse>(
            "/api/oauth/github/status"
        );
        body.Should().NotBeNull();
        body!.Connected.Should().BeTrue();
        body.AccountLogin.Should().Be("octocat");
        body.Scopes.Should().Be("read:user public_repo");
    }

    [Fact]
    public async Task TokenAccessorReturnsTokenWhenConnected()
    {
        using ShieldWebAppFactory factory = new();
        // Force factory to spin up the host.
        factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "raw-access-token",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "public_repo",
                AccountLogin: "octocat",
                AccountId: null,
                Extra: null
            )
        );

        IOAuthTokenAccessor accessor = factory.Services.GetRequiredService<IOAuthTokenAccessor>();
        string? token = await accessor.GetAccessTokenAsync(OAuthProvider.Github);
        token.Should().Be("raw-access-token");
    }

    [Fact]
    public async Task TokensAreEncryptedAtRest()
    {
        using ShieldWebAppFactory factory = new();
        factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Slack,
                AccessToken: "plaintext-bot-token",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "chat:write",
                AccountLogin: "shield-test",
                AccountId: "T123",
                Extra: null
            )
        );

        // Read raw row through DbContext — value must not equal the plaintext token.
        using IServiceScope scope = factory.Services.CreateScope();
        Data.ShieldDbContext db = scope.ServiceProvider.GetRequiredService<Data.ShieldDbContext>();
        IntegrationToken? row = db.IntegrationTokens.FirstOrDefault(t =>
            t.Provider == OAuthProvider.Slack
        );
        row.Should().NotBeNull();
        row!.AccessTokenEncrypted.Should().NotBeNullOrEmpty();
        row.AccessTokenEncrypted.Should().NotContain("plaintext-bot-token");
    }

    [Fact]
    public async Task DisconnectClearsToken()
    {
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Slack,
                AccessToken: "xoxb-test",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "chat:write",
                AccountLogin: "shield-test",
                AccountId: null,
                Extra: null
            )
        );

        HttpResponseMessage disconnect = await client.PostAsync(
            "/api/oauth/slack/disconnect",
            content: null
        );
        disconnect.StatusCode.Should().Be(HttpStatusCode.NoContent);

        OAuthStatusResponse? after = await client.GetFromJsonAsync<OAuthStatusResponse>(
            "/api/oauth/slack/status"
        );
        after!.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task StartReturnsBadRequestWhenClientIdNotConfigured()
    {
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/oauth/github/start");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StartReturnsAuthorizationUrlWhenConfigured()
    {
        using ConfiguredOAuthFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/oauth/github/start");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        OAuthStartResponse? body = await response.Content.ReadFromJsonAsync<OAuthStartResponse>();
        body.Should().NotBeNull();
        body!.AuthorizationUrl.Should().StartWith(GitHubProvider.AuthorizeUrl);
        body.AuthorizationUrl.Should().Contain("client_id=test-client-id");
        body.AuthorizationUrl.Should().Contain("code_challenge=");
        body.AuthorizationUrl.Should().Contain("code_challenge_method=S256");
        body.State.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CallbackWithInvalidStateRedirectsToSettingsWithError()
    {
        using ConfiguredOAuthFactory factory = new();
        HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync(
            "/api/oauth/github/callback?code=abc&state=bogus"
        );
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response
            .Headers.Location!.OriginalString.Should()
            .Contain("/settings?oauth_error=invalid_state");
    }

    private sealed class ConfiguredOAuthFactory : ShieldWebAppFactory
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
                            ["Shield:OAuth:Github:ClientId"] = "test-client-id",
                            ["Shield:OAuth:Github:ClientSecret"] = "test-client-secret",
                            ["Shield:OAuth:RedirectBase"] = "http://localhost:8080",
                        }
                    );
                }
            );
        }
    }

    // -------- signin flow tests --------

    [Fact]
    public async Task ProvidersListOnlyShowsConfigured()
    {
        using SigninFactory factory = new(github: true, slack: false, google: false);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/providers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AuthProvidersResponse? body =
            await response.Content.ReadFromJsonAsync<AuthProvidersResponse>();
        body.Should().NotBeNull();
        body!.Providers.Should().HaveCount(1);
        body.Providers[0].Provider.Should().Be("github");
        body.Providers[0].DisplayName.Should().Be("GitHub");
    }

    [Fact]
    public async Task SigninCreatesFirstUserAsAdmin()
    {
        using SigninFactory factory = new(
            github: true,
            slack: false,
            google: false,
            singleUser: false
        );
        HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });

        IOAuthStateStore stateStore = factory.Services.GetRequiredService<IOAuthStateStore>();
        const string state = "test-state-first";
        stateStore.Save(
            state,
            new(
                OAuthProvider.Github,
                CodeVerifier: "v",
                ReturnUrl: "/",
                ExpiresAt: DateTime.UtcNow.AddMinutes(5),
                Intent: OAuthIntent.Signin
            )
        );
        FakeOAuthProvider.NextResult = new(
            Subject: "github-id-1001",
            Login: "first-admin",
            Email: "first@example.com",
            Token: new(
                OAuthProvider.Github,
                AccessToken: "fake-access",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user",
                AccountLogin: "first-admin",
                AccountId: "github-id-1001",
                Extra: null
            )
        );

        HttpResponseMessage response = await client.GetAsync(
            $"/api/oauth/github/callback?code=abc&state={state}"
        );
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Verify the user was created with Admin role.
        using IServiceScope scope = factory.Services.CreateScope();
        Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser>>();
        Data.Identity.ShieldUser? user = await users.FindByEmailAsync("first@example.com");
        user.Should().NotBeNull();
        IList<string> roles = await users.GetRolesAsync(user!);
        roles.Should().Contain("Admin");

        // Verify the cookie was set: follow up /auth/me on the same HttpClient (cookie-aware by
        // default in WebApplicationFactory) should return 200 with the same user.
        HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResponse? meBody = await me.Content.ReadFromJsonAsync<MeResponse>();
        meBody!.UserId.Should().Be(user!.Id.ToString());
    }

    [Fact]
    public async Task SigninFindsExistingUserByEmail()
    {
        using SigninFactory factory = new(
            github: true,
            slack: false,
            google: false,
            singleUser: false
        );
        HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Seed a real user via the registration endpoint (first-user bootstrap path).
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("seed-admin", "P@ssword1", "seed@example.com")
        );
        await client.PostAsync("/api/auth/logout", content: null);

        IOAuthStateStore stateStore = factory.Services.GetRequiredService<IOAuthStateStore>();
        const string state = "test-state-link";
        stateStore.Save(
            state,
            new(
                OAuthProvider.Github,
                CodeVerifier: "v",
                ReturnUrl: "/",
                ExpiresAt: DateTime.UtcNow.AddMinutes(5),
                Intent: OAuthIntent.Signin
            )
        );
        FakeOAuthProvider.NextResult = new(
            Subject: "github-id-link",
            Login: "seed-admin-external",
            Email: "seed@example.com",
            Token: new(
                OAuthProvider.Github,
                AccessToken: "fake-access",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user",
                AccountLogin: "seed-admin-external",
                AccountId: "github-id-link",
                Extra: null
            )
        );

        HttpResponseMessage response = await client.GetAsync(
            $"/api/oauth/github/callback?code=abc&state={state}"
        );
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using IServiceScope scope = factory.Services.CreateScope();
        Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser>>();
        // Existing user found by email — no synthetic provider+login duplicate created.
        Data.Identity.ShieldUser? seeded = await users.FindByNameAsync("seed-admin");
        seeded.Should().NotBeNull();
        Data.Identity.ShieldUser? duplicate = await users.FindByNameAsync(
            "githubseedadminexternal"
        );
        duplicate.Should().BeNull();
    }

    [Fact]
    public async Task SigninRejectedWhenRegistrationClosedAndNoMatch()
    {
        using SigninFactory factory = new(
            github: true,
            slack: false,
            google: false,
            singleUser: false
        );
        HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Bootstrap an admin so we're past the "first user" branch; RegistrationOpen stays false.
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("bootstrap-admin", "P@ssword1", "bootstrap@example.com")
        );
        await client.PostAsync("/api/auth/logout", content: null);

        IOAuthStateStore stateStore = factory.Services.GetRequiredService<IOAuthStateStore>();
        const string state = "test-state-rejected";
        stateStore.Save(
            state,
            new(
                OAuthProvider.Github,
                CodeVerifier: "v",
                ReturnUrl: "/login",
                ExpiresAt: DateTime.UtcNow.AddMinutes(5),
                Intent: OAuthIntent.Signin
            )
        );
        FakeOAuthProvider.NextResult = new(
            Subject: "github-id-reject",
            Login: "stranger",
            Email: "stranger@example.com",
            Token: new(
                OAuthProvider.Github,
                AccessToken: "fake-access",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user",
                AccountLogin: "stranger",
                AccountId: "github-id-reject",
                Extra: null
            )
        );

        HttpResponseMessage response = await client.GetAsync(
            $"/api/oauth/github/callback?code=abc&state={state}"
        );
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/login");
        response
            .Headers.Location.OriginalString.Should()
            .Contain("oauth_signin_rejected=oauth_signin_rejected");

        using IServiceScope scope = factory.Services.CreateScope();
        Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser> users =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.Identity.ShieldUser>>();
        Data.Identity.ShieldUser? rejected = await users.FindByEmailAsync("stranger@example.com");
        rejected.Should().BeNull();
    }

    // Overrides the live github adapter so callback tests don't hit github.com.
    private sealed class SigninFactory : ShieldWebAppFactory
    {
        private readonly bool _github;
        private readonly bool _slack;
        private readonly bool _google;
        private readonly bool _singleUser;

        public SigninFactory(bool github, bool slack, bool google, bool singleUser = true)
        {
            _github = github;
            _slack = slack;
            _google = google;
            _singleUser = singleUser;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    Dictionary<string, string?> overrides = new()
                    {
                        ["Shield:SingleUser"] = _singleUser ? "true" : "false",
                        ["Shield:OAuth:RedirectBase"] = "http://localhost:8080",
                    };
                    if (_github)
                    {
                        overrides["Shield:OAuth:Github:ClientId"] = "test-github-id";
                        overrides["Shield:OAuth:Github:ClientSecret"] = "test-github-secret";
                    }
                    if (_slack)
                    {
                        overrides["Shield:OAuth:Slack:ClientId"] = "test-slack-id";
                        overrides["Shield:OAuth:Slack:ClientSecret"] = "test-slack-secret";
                    }
                    if (_google)
                    {
                        overrides["Shield:OAuth:Google:ClientId"] = "test-google-id";
                        overrides["Shield:OAuth:Google:ClientSecret"] = "test-google-secret";
                    }
                    config.AddInMemoryCollection(overrides);
                }
            );

            builder.ConfigureServices(services =>
            {
                ServiceDescriptor[] real = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IOAuthProvider)
                        && descriptor.ImplementationType == typeof(GitHubProvider)
                    )
                    .ToArray();
                foreach (ServiceDescriptor descriptor in real)
                    services.Remove(descriptor);
                services.AddSingleton<IOAuthProvider, FakeOAuthProvider>();
            });
        }
    }

    // Test double. Tests stage NextResult; the controller's signin callback consumes it.
    private sealed class FakeOAuthProvider : IOAuthProvider
    {
        public static OAuthSigninResult? NextResult { get; set; }

        public OAuthProvider Provider => OAuthProvider.Github;
        public string DefaultScopes => "read:user";
        public string SigninDefaultScopes => "read:user user:email";
        public bool SupportsPkce => true;

        public string BuildAuthorizationUrl(
            OAuthClientConfig config,
            string state,
            string codeChallenge,
            string scopes
        ) => "https://example.com/auth?state=" + state;

        public string BuildSigninAuthorizationUrl(
            OAuthClientConfig config,
            string state,
            string codeChallenge,
            string scopes
        ) => "https://example.com/auth?state=" + state;

        public Task<OAuthTokenSnapshot> ExchangeCodeAsync(
            OAuthClientConfig config,
            string code,
            string codeVerifier,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new OAuthTokenSnapshot(
                    OAuthProvider.Github,
                    "fake",
                    null,
                    null,
                    "read:user",
                    "fake",
                    "1",
                    null
                )
            );

        public Task<OAuthSigninResult> ExchangeCodeForSigninAsync(
            OAuthClientConfig config,
            string code,
            string codeVerifier,
            CancellationToken ct
        )
        {
            OAuthSigninResult result =
                NextResult
                ?? throw new InvalidOperationException("FakeOAuthProvider.NextResult was not set");
            NextResult = null;
            return Task.FromResult(result);
        }

        public Task<OAuthTokenSnapshot?> RefreshAsync(
            OAuthClientConfig config,
            OAuthTokenSnapshot current,
            CancellationToken ct
        ) => Task.FromResult<OAuthTokenSnapshot?>(null);

        public Task RevokeAsync(
            OAuthClientConfig config,
            OAuthTokenSnapshot token,
            CancellationToken ct
        ) => Task.CompletedTask;
    }
}
