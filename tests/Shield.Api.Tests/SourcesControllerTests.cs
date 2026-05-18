using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Data.Identity;
using Xunit;

namespace Shield.Api.Tests;

// Auth permutation tests for POST /api/sources/{id}/apply-all-fixes.
// Admin (SingleUser mode) → 400 (wrong source type, but auth passes).
// Non-admin viewer → 403.
// API token bearer → 403 ([NoApiToken] gate).
public sealed class SourcesControllerTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public SourcesControllerTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApplyAllFixesAdminCanReachEndpoint()
    {
        int sourceId = await SeedGithubSourceAsync("ctrl-admin-fixture");

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );

        // Admin hits the endpoint; dry-run on a GithubRepo source with no findings returns 200.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApplyAllFixesNonAdminReturns403()
    {
        await using ViewerRoleFactory factory = new();

        // Seed source and viewer user directly via EF — bypasses the HTTP layer so we
        // don't need a working admin HTTP session on a SingleUser=false factory.
        int sourceId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Source source = new()
            {
                Type = SourceType.GithubRepo,
                Name = "ctrl-viewer-source",
                ConfigJson = "{\"owner\":\"test\",\"repo\":\"fixture\"}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Sources.Add(source);
            await db.SaveChangesAsync();
            sourceId = source.Id;

            Microsoft.AspNetCore.Identity.UserManager<ShieldUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ShieldUser>>();
            ShieldUser viewer = new()
            {
                UserName = "ctrl-viewer",
                Email = "viewer@test",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
            };
            await userManager.CreateAsync(viewer, "Viewer1Pass!");
            await userManager.AddToRoleAsync(viewer, "Maintainer");
        }

        HttpClient viewerClient = factory.CreateClient();
        await viewerClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("ctrl-viewer", "Viewer1Pass!")
        );

        HttpResponseMessage response = await viewerClient.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApplyAllFixesApiTokenBearerReturns403()
    {
        int sourceId = await SeedGithubSourceAsync("ctrl-apitoken-fixture");

        // Mint an API token as admin.
        HttpClient adminClient = _factory.CreateClient();
        HttpResponseMessage tokenResponse = await adminClient.PostAsJsonAsync(
            "/api/apitokens",
            new CreateApiTokenRequest("ctrl-no-apitoken", ["sources:read"], null, null)
        );
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        CreateApiTokenResponse? tokenBody =
            await tokenResponse.Content.ReadFromJsonAsync<CreateApiTokenResponse>();
        tokenBody.Should().NotBeNull();

        // Use the API token bearer — [NoApiToken] gate must block it.
        HttpClient tokenClient = _factory.CreateClient();
        tokenClient.DefaultRequestHeaders.Authorization = new("Bearer", tokenBody!.Plaintext);

        HttpResponseMessage response = await tokenClient.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApplyAllFixesUnknownSourceReturns404()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/sources/999999/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApplyAllFixesLocalFolderSourceReturns400()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/sources",
            new
            {
                type = (int)SourceType.LocalFolder,
                name = "ctrl-local-bulk",
                configJson = new { path = "/tmp" },
                scanInterval = "01:00:00",
            }
        );
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{created!.Id}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<int> SeedGithubSourceAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        Source source = new()
        {
            Type = SourceType.GithubRepo,
            Name = name,
            ConfigJson = "{\"owner\":\"test\",\"repo\":\"fixture\"}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    // SingleUser=false so role-based gates see real Identity principals.
    private sealed class ViewerRoleFactory : ShieldWebAppFactory
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
