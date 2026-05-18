using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services.AppSettings;
using Shield.Api.Services.Auth;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class GitHubDeviceFlowTests
{
    [Fact]
    public async Task StartReturnsFlowIdUserCodeAndVerificationUri()
    {
        await using DeviceFlowFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();
        FakeGitHubDeviceFlowClient.NextDeviceCode = new(
            DeviceCode: "device-code-xyz",
            UserCode: "WDJB-MJHT",
            VerificationUri: "https://github.com/login/device",
            ExpiresIn: 900,
            Interval: 5
        );

        HttpResponseMessage response = await client.PostAsync(
            "/api/oauth/github/device/start",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        GitHubDeviceStartResponse? body =
            await response.Content.ReadFromJsonAsync<GitHubDeviceStartResponse>();
        body.Should().NotBeNull();
        body!.FlowId.Should().NotBeNullOrEmpty();
        body.UserCode.Should().Be("WDJB-MJHT");
        body.VerificationUri.Should().Be("https://github.com/login/device");
        body.ExpiresIn.Should().Be(900);
        body.Interval.Should().Be(5);

        // device_code should be cached server-side under the issued flowId, never returned to the SPA.
        IGitHubDeviceFlowStore store =
            factory.Services.GetRequiredService<IGitHubDeviceFlowStore>();
        GitHubDeviceFlowEntry? entry = store.Find(body.FlowId);
        entry.Should().NotBeNull();
        entry!.DeviceCode.Should().Be("device-code-xyz");
    }

    [Fact]
    public async Task PollReturnsPendingWhileUserHasNotAuthorized()
    {
        await using DeviceFlowFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();
        FakeGitHubDeviceFlowClient.NextDeviceCode = new(
            "device-code-pending",
            "USR-PEND",
            "https://github.com/login/device",
            900,
            5
        );

        HttpResponseMessage startResponse = await client.PostAsync(
            "/api/oauth/github/device/start",
            content: null
        );
        GitHubDeviceStartResponse start = (
            await startResponse.Content.ReadFromJsonAsync<GitHubDeviceStartResponse>()
        )!;

        FakeGitHubDeviceFlowClient.NextTokenResponse = new(
            AccessToken: null,
            Scope: null,
            TokenType: null,
            Error: "authorization_pending",
            ErrorDescription: "The user has not yet entered the code."
        );

        HttpResponseMessage pollResponse = await client.PostAsJsonAsync(
            "/api/oauth/github/device/poll",
            new GitHubDevicePollRequest(start.FlowId)
        );
        pollResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        GitHubDevicePollResponse? body =
            await pollResponse.Content.ReadFromJsonAsync<GitHubDevicePollResponse>();
        body!.Status.Should().Be("pending");

        // flowId is NOT dropped on pending — the SPA keeps polling.
        IGitHubDeviceFlowStore store =
            factory.Services.GetRequiredService<IGitHubDeviceFlowStore>();
        store.Find(start.FlowId).Should().NotBeNull();
    }

    [Fact]
    public async Task PollReturnsOkAndWritesIntegrationTokenOnSuccess()
    {
        await using DeviceFlowFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();
        FakeGitHubDeviceFlowClient.NextDeviceCode = new(
            "device-code-success",
            "GR8T-PASS",
            "https://github.com/login/device",
            900,
            5
        );

        HttpResponseMessage startResponse = await client.PostAsync(
            "/api/oauth/github/device/start",
            content: null
        );
        GitHubDeviceStartResponse start = (
            await startResponse.Content.ReadFromJsonAsync<GitHubDeviceStartResponse>()
        )!;

        FakeGitHubDeviceFlowClient.NextTokenResponse = new(
            AccessToken: "gho_real_access_token_value",
            Scope: "read:user public_repo",
            TokenType: "bearer",
            Error: null,
            ErrorDescription: null
        );
        FakeGitHubDeviceFlowClient.NextUserProfile = new(
            Login: "octocat",
            Id: "1",
            AvatarUrl: "https://github.com/images/error/octocat_happy.gif"
        );

        HttpResponseMessage pollResponse = await client.PostAsJsonAsync(
            "/api/oauth/github/device/poll",
            new GitHubDevicePollRequest(start.FlowId)
        );
        pollResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        GitHubDevicePollResponse? body =
            await pollResponse.Content.ReadFromJsonAsync<GitHubDevicePollResponse>();
        body!.Status.Should().Be("ok");
        body.User.Should().NotBeNull();
        body.User!.Login.Should().Be("octocat");
        body.User.Id.Should().Be("1");

        // IntegrationToken row should be persisted with the connect-flow Subject="" and a LinkedUserId.
        using IServiceScope scope = factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IntegrationToken? row = db.IntegrationTokens.FirstOrDefault(token =>
            token.Provider == OAuthProvider.Github && token.Subject == ""
        );
        row.Should().NotBeNull();
        row!.AccountLogin.Should().Be("octocat");
        row.AccountId.Should().Be("1");
        row.Scopes.Should().Be("read:user public_repo");
        row.LinkedUserId.Should().NotBeNull();
        row.AccessTokenEncrypted.Should().NotContain("gho_real_access_token_value");

        // Token is reachable through the standard accessor (workers / PR comments).
        IOAuthTokenAccessor accessor = factory.Services.GetRequiredService<IOAuthTokenAccessor>();
        string? plain = await accessor.GetAccessTokenAsync(OAuthProvider.Github);
        plain.Should().Be("gho_real_access_token_value");

        // flowId is dropped on success so a replay can't extract the device_code again.
        IGitHubDeviceFlowStore store =
            factory.Services.GetRequiredService<IGitHubDeviceFlowStore>();
        store.Find(start.FlowId).Should().BeNull();
    }

    [Fact]
    public async Task PollReturnsExpiredAndDropsFlowOnExpiredToken()
    {
        await using DeviceFlowFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();
        FakeGitHubDeviceFlowClient.NextDeviceCode = new(
            "device-code-expired",
            "EXP1-RED2",
            "https://github.com/login/device",
            900,
            5
        );

        HttpResponseMessage startResponse = await client.PostAsync(
            "/api/oauth/github/device/start",
            content: null
        );
        GitHubDeviceStartResponse start = (
            await startResponse.Content.ReadFromJsonAsync<GitHubDeviceStartResponse>()
        )!;

        FakeGitHubDeviceFlowClient.NextTokenResponse = new(
            AccessToken: null,
            Scope: null,
            TokenType: null,
            Error: "expired_token",
            ErrorDescription: "The device_code has expired."
        );

        HttpResponseMessage pollResponse = await client.PostAsJsonAsync(
            "/api/oauth/github/device/poll",
            new GitHubDevicePollRequest(start.FlowId)
        );
        pollResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
        GitHubDevicePollResponse? body =
            await pollResponse.Content.ReadFromJsonAsync<GitHubDevicePollResponse>();
        body!.Status.Should().Be("expired");

        IGitHubDeviceFlowStore store =
            factory.Services.GetRequiredService<IGitHubDeviceFlowStore>();
        store.Find(start.FlowId).Should().BeNull();
    }

    [Fact]
    public async Task StatusAdvertisesDeviceFlowAvailableWhenDefaultClientIdResolves()
    {
        await using DeviceFlowFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        OAuthStatusResponse? body = await client.GetFromJsonAsync<OAuthStatusResponse>(
            "/api/oauth/github/status"
        );
        body.Should().NotBeNull();
        body!.DeviceFlowAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task PerUserClientIdOverrideWinsOverDefault()
    {
        await using DeviceFlowFactory factory = new();
        await factory.InitializeAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();
        FakeGitHubDeviceFlowClient.NextDeviceCode = new(
            "device-code-override",
            "OVR-CODE",
            "https://github.com/login/device",
            900,
            5
        );

        // Per-user override stored via settings — write it then start the flow and inspect
        // which client_id the fake client received.
        IAppSettingsService settings = factory.Services.GetRequiredService<IAppSettingsService>();
        await settings.UpdateAsync(
            new(
                OpenApiEnabled: false,
                OidcEnabled: false,
                OidcIssuer: null,
                OidcClientId: null,
                OidcClientSecret: null,
                PreserveOidcClientSecret: true,
                AlertSeverityFloor: Severity.Low,
                RetentionDays: 90,
                RegistrationOpen: false,
                GithubOAuth: new(
                    ClientId: "operator-supplied-client-id",
                    ClientSecret: null,
                    PreserveClientSecret: true,
                    Scopes: null
                ),
                SlackOAuth: new(null, null, true, null),
                GoogleOAuth: new(null, null, true, null),
                GitlabOAuth: new(null, null, true, null),
                BitbucketOAuth: new(null, null, true, null),
                ForgejoOAuth: new(null, null, true, null),
                GiteaOAuth: new(null, null, true, null),
                CodebergOAuth: new(null, null, true, null),
                OAuthRedirectBase: null
            ),
            updatedBy: null
        );

        HttpResponseMessage response = await client.PostAsync(
            "/api/oauth/github/device/start",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FakeGitHubDeviceFlowClient.LastClientIdUsed.Should().Be("operator-supplied-client-id");
    }

    // Shared factory — substitutes the device-flow client + clears per-test fake state in the ctor.
    private sealed class DeviceFlowFactory : ShieldWebAppFactory
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
                            // Bake the public app id in for tests so the default-resolution path is exercised.
                            ["Shield:OAuth:GitHub:DefaultClientId"] = "Ov23libI6hv5NBmkaxjV",
                            ["Shield:OAuth:GitHub:DeviceFlow:Enabled"] = "true",
                        }
                    );
                }
            );

            builder.ConfigureServices(services =>
            {
                ServiceDescriptor[] descriptors = services
                    .Where(d => d.ServiceType == typeof(IGitHubDeviceFlowClient))
                    .ToArray();
                foreach (ServiceDescriptor descriptor in descriptors)
                    services.Remove(descriptor);
                services.AddSingleton<IGitHubDeviceFlowClient, FakeGitHubDeviceFlowClient>();
            });

            FakeGitHubDeviceFlowClient.Reset();
        }
    }

    // Test double — tests stage NextDeviceCode / NextTokenResponse / NextUserProfile and the
    // controller calls into the appropriate method. LastClientIdUsed lets tests assert that
    // overrides win over the default.
    private sealed class FakeGitHubDeviceFlowClient : IGitHubDeviceFlowClient
    {
        public static GitHubDeviceCodeResponse? NextDeviceCode { get; set; }
        public static GitHubDeviceTokenResponse? NextTokenResponse { get; set; }
        public static GitHubUserProfile? NextUserProfile { get; set; }
        public static string? LastClientIdUsed { get; set; }

        public static void Reset()
        {
            NextDeviceCode = null;
            NextTokenResponse = null;
            NextUserProfile = null;
            LastClientIdUsed = null;
        }

        public Task<GitHubDeviceCodeResponse> RequestDeviceCodeAsync(
            string clientId,
            string scopes,
            CancellationToken ct
        )
        {
            LastClientIdUsed = clientId;
            GitHubDeviceCodeResponse code =
                NextDeviceCode
                ?? throw new InvalidOperationException(
                    "FakeGitHubDeviceFlowClient.NextDeviceCode was not set"
                );
            return Task.FromResult(code);
        }

        public Task<GitHubDeviceTokenResponse> PollAccessTokenAsync(
            string clientId,
            string deviceCode,
            CancellationToken ct
        )
        {
            GitHubDeviceTokenResponse response =
                NextTokenResponse
                ?? throw new InvalidOperationException(
                    "FakeGitHubDeviceFlowClient.NextTokenResponse was not set"
                );
            return Task.FromResult(response);
        }

        public Task<GitHubUserProfile?> FetchUserProfileAsync(
            string accessToken,
            CancellationToken ct
        ) => Task.FromResult(NextUserProfile);
    }
}
