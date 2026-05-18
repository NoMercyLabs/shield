using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SettingsTests
{
    [Fact]
    public async Task Get_returns_defaults_when_db_empty()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-reader");

        HttpResponseMessage response = await client.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SettingsResponse? settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        settings.Should().NotBeNull();
        settings!.SingleUserMode.Should().BeFalse();
        settings.OpenApiEnabled.Should().BeFalse();
        settings.OidcEnabled.Should().BeFalse();
        settings.OidcClientSecretMasked.Should().BeNull();
        settings.AlertSeverityFloor.Should().Be(Severity.Low);
        settings.RetentionDays.Should().Be(90);
        settings.Github.Should().NotBeNull();
        settings.Github.ClientId.Should().BeNull();
        settings.Github.ClientSecretMasked.Should().BeNull();
        settings.Github.Scopes.Should().BeNull();
        settings.Github.Configured.Should().BeFalse();
        settings.Slack.Configured.Should().BeFalse();
        settings.Google.Configured.Should().BeFalse();
    }

    [Fact]
    public async Task GetSettings_returns_oauth_provider_config_masked()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-oauth-get");

        UpdateSettingsRequest seed = BaselineRequest() with
        {
            Github = new(
                ClientId: "gh-client-id",
                ClientSecret: "ghp_supersecret_abcd",
                Scopes: "read:user repo"
            ),
        };
        HttpResponseMessage putResponse = await client.PutAsJsonAsync("/api/settings", seed);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SettingsResponse? reread = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        reread.Should().NotBeNull();
        reread!.Github.ClientId.Should().Be("gh-client-id");
        reread.Github.ClientSecretMasked.Should().Be("****abcd");
        reread.Github.Scopes.Should().Be("read:user repo");
        reread.Github.Configured.Should().BeTrue();
    }

    [Fact]
    public async Task PutSettings_persists_provider_credentials_and_masks_on_read_back()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-oauth-put");

        UpdateSettingsRequest request = BaselineRequest() with
        {
            Github = new("gh-id", "ghs_supersecret_wxyz", "read:user"),
            Slack = new("slack-id", "xoxa-1234abcd", "chat:write"),
            Google = new("g-id", "google-secret-mnop", "openid email"),
        };

        HttpResponseMessage putResponse = await client.PutAsJsonAsync("/api/settings", request);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        UpdateSettingsResponse? body =
            await putResponse.Content.ReadFromJsonAsync<UpdateSettingsResponse>();
        body.Should().NotBeNull();
        body!.Settings.Github.Configured.Should().BeTrue();
        body.Settings.Github.ClientSecretMasked.Should().Be("****wxyz");
        body.Settings.Slack.ClientSecretMasked.Should().Be("****abcd");
        body.Settings.Google.ClientSecretMasked.Should().Be("****mnop");

        SettingsResponse? reread = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        reread!.Github.ClientId.Should().Be("gh-id");
        reread.Slack.ClientId.Should().Be("slack-id");
        reread.Google.ClientId.Should().Be("g-id");
        reread.Github.ClientSecretMasked.Should().NotContain("supersecret");
    }

    [Fact]
    public async Task PutSettings_with_null_clientSecret_keeps_existing()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-oauth-keep");

        UpdateSettingsRequest seed = BaselineRequest() with
        {
            Github = new("gh-id", "original-secret-1234", "read:user"),
        };
        await client.PutAsJsonAsync("/api/settings", seed);

        // Second PUT with null secret — should preserve original.
        UpdateSettingsRequest update = BaselineRequest() with
        {
            Github = new("gh-id-updated", null, "read:user repo"),
        };
        await client.PutAsJsonAsync("/api/settings", update);

        SettingsResponse? reread = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        reread!.Github.ClientId.Should().Be("gh-id-updated");
        reread.Github.ClientSecretMasked.Should().Be("****1234");
        reread.Github.Configured.Should().BeTrue();
        reread.Github.Scopes.Should().Be("read:user repo");
    }

    [Fact]
    public async Task PutSettings_with_empty_clientSecret_clears()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-oauth-clear");

        UpdateSettingsRequest seed = BaselineRequest() with
        {
            Github = new("gh-id", "original-secret-1234", "read:user"),
        };
        await client.PutAsJsonAsync("/api/settings", seed);

        UpdateSettingsRequest clear = BaselineRequest() with
        {
            Github = new("gh-id", "", "read:user"),
        };
        await client.PutAsJsonAsync("/api/settings", clear);

        SettingsResponse? reread = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        reread!.Github.ClientId.Should().Be("gh-id");
        reread.Github.ClientSecretMasked.Should().BeNull();
        reread.Github.Configured.Should().BeFalse();
    }

    private static UpdateSettingsRequest BaselineRequest() =>
        new(
            SingleUserMode: false,
            OpenApiEnabled: false,
            OidcEnabled: false,
            OidcIssuer: null,
            OidcClientId: null,
            OidcClientSecret: null,
            AlertSeverityFloor: Severity.Low,
            RetentionDays: 90
        );

    [Fact]
    public async Task Put_persists_values_and_masks_secret_on_read_back()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-writer");

        UpdateSettingsRequest request = new(
            SingleUserMode: true,
            OpenApiEnabled: true,
            OidcEnabled: true,
            OidcIssuer: "https://issuer.example/realms/shield",
            OidcClientId: "shield-client",
            OidcClientSecret: "super-secret-value-123",
            AlertSeverityFloor: Severity.High,
            RetentionDays: 45
        );

        HttpResponseMessage putResponse = await client.PutAsJsonAsync("/api/settings", request);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        UpdateSettingsResponse? updated =
            await putResponse.Content.ReadFromJsonAsync<UpdateSettingsResponse>();
        updated.Should().NotBeNull();
        updated!.RequiresRestart.Should().BeTrue();
        updated.RestartKeys.Should().Contain("openApiEnabled");
        updated.Settings.OidcIssuer.Should().Be("https://issuer.example/realms/shield");
        updated.Settings.OidcClientId.Should().Be("shield-client");
        updated.Settings.OidcClientSecretMasked.Should().NotBeNullOrEmpty();
        updated.Settings.OidcClientSecretMasked.Should().NotContain("super-secret");
        updated.Settings.OidcClientSecretMasked.Should().EndWith("-123");
        updated.Settings.AlertSeverityFloor.Should().Be(Severity.High);
        updated.Settings.RetentionDays.Should().Be(45);

        SettingsResponse? reread = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        reread.Should().NotBeNull();
        reread!.OidcIssuer.Should().Be("https://issuer.example/realms/shield");
        reread.RetentionDays.Should().Be(45);
        reread.AlertSeverityFloor.Should().Be(Severity.High);
    }

    [Fact]
    public async Task Runtime_returns_environment_info()
    {
        using SettingsFactory factory = new();
        HttpClient client = await LoginAsAdminAsync(factory, "settings-runtime");

        HttpResponseMessage response = await client.GetAsync("/api/settings/runtime");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        RuntimeInfoResponse? runtime =
            await response.Content.ReadFromJsonAsync<RuntimeInfoResponse>();
        runtime.Should().NotBeNull();
        runtime!.Environment.Should().Be("Testing");
        runtime.ContentRoot.Should().NotBeNullOrEmpty();
        runtime.Version.Should().NotBeNullOrEmpty();
    }

    private static async Task<HttpClient> LoginAsAdminAsync(
        SettingsFactory factory,
        string username
    )
    {
        HttpClient client = factory.CreateClient();
        // First registration becomes Admin per AuthController logic.
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(username, "Correct1!")
        );
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "Correct1!")
        );
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    // Multi-user mode lets the real cookie auth flow run; SingleUser middleware bypasses it otherwise.
    private sealed class SettingsFactory : ShieldWebAppFactory
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
