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
    }

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

        UpdateSettingsResponse? updated = await putResponse.Content.ReadFromJsonAsync<UpdateSettingsResponse>();
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

        RuntimeInfoResponse? runtime = await response.Content.ReadFromJsonAsync<RuntimeInfoResponse>();
        runtime.Should().NotBeNull();
        runtime!.Environment.Should().Be("Testing");
        runtime.ContentRoot.Should().NotBeNullOrEmpty();
        runtime.Version.Should().NotBeNullOrEmpty();
    }

    private static async Task<HttpClient> LoginAsAdminAsync(SettingsFactory factory, string username)
    {
        HttpClient client = factory.CreateClient();
        // First registration becomes Admin per AuthController logic.
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "Correct1!"));
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
