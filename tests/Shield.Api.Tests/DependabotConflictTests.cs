using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shield.Api.Services.BulkFix;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Confirms the bulk-apply orchestrator blocks the click when Dependabot already has open PRs
// in the repo, surfaces the PR list in the 409 payload, and proceeds when the caller
// re-submits with acknowledgeDependabotConflict=true.
public sealed class DependabotConflictTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public DependabotConflictTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BlocksWhenDependabotHasOpenPrs()
    {
        FakeDependabotPrLookup fake = new();
        fake.OpenPrs.Add(
            new DependabotPrSummary(
                42,
                "Bump lodash from 4.17.20 to 4.17.21",
                "https://github.com/test/repo/pull/42",
                DateTime.UtcNow.AddHours(-3)
            )
        );

        WebApplicationFactory<Program> factoryWithFake = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ServiceDescriptor[] toRemove = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IDependabotPrLookup))
                    .ToArray();
                foreach (ServiceDescriptor descriptor in toRemove)
                    services.Remove(descriptor);
                services.AddScoped<IDependabotPrLookup>(_ => fake);
            })
        );

        int sourceId = await SeedGithubSourceAsync("conflict-block");
        await SeedOpenFindingAsync(sourceId);

        HttpClient client = await CreateAuthenticatedClientAsync(factoryWithFake);
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = false }
        );

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("dependabot_conflict");
        JsonElement openPrs = body.RootElement.GetProperty("openPrs");
        openPrs.GetArrayLength().Should().Be(1);
        openPrs[0].GetProperty("number").GetInt32().Should().Be(42);
        openPrs[0].GetProperty("htmlUrl").GetString().Should().Contain("/pull/42");
    }

    [Fact]
    public async Task AcknowledgeFlagBypassesTheGate()
    {
        FakeDependabotPrLookup fake = new();
        fake.OpenPrs.Add(
            new DependabotPrSummary(
                7,
                "Bump axios from 1.6.0 to 1.7.0",
                "https://github.com/test/repo/pull/7",
                DateTime.UtcNow.AddHours(-1)
            )
        );

        WebApplicationFactory<Program> factoryWithFake = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ServiceDescriptor[] toRemove = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IDependabotPrLookup))
                    .ToArray();
                foreach (ServiceDescriptor descriptor in toRemove)
                    services.Remove(descriptor);
                services.AddScoped<IDependabotPrLookup>(_ => fake);
            })
        );

        int sourceId = await SeedGithubSourceAsync("conflict-ack");
        await SeedOpenFindingAsync(sourceId);

        HttpClient client = await CreateAuthenticatedClientAsync(factoryWithFake);
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = false, acknowledgeDependabotConflict = true }
        );

        // The orchestrator proceeds; whether the underlying applier emits a PR URL depends on
        // the test env (no GitHub token), but we only need to assert that the conflict gate
        // did NOT trip a 409 this time.
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
    }

    private async Task<int> SeedGithubSourceAsync(string nameSuffix)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Source source = new()
        {
            Name = $"test/{nameSuffix}-{Guid.NewGuid():N}",
            Type = SourceType.GithubRepo,
            ConfigJson = "{}",
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task SeedOpenFindingAsync(int sourceId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        db.Findings.Add(
            new()
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                AdvisoryRefId = Guid.NewGuid(),
                State = FindingState.Open,
                Severity = Severity.High,
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                DedupKey = Guid.NewGuid().ToString("N"),
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory
    )
    {
        // Reuse the same login flow as ShieldWebAppFactory.CreateAuthenticatedClientAsync but
        // bound to the override factory so the request hits the same services we patched.
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new Shield.Api.Contracts.LoginRequest(
                ShieldWebAppFactory.AdminUsername,
                ShieldWebAppFactory.AdminPassword
            )
        );
        return client;
    }

    private sealed class FakeDependabotPrLookup : IDependabotPrLookup
    {
        public List<DependabotPrSummary> OpenPrs { get; } = [];

        public Task<IReadOnlyList<DependabotPrSummary>> ListOpenAsync(
            string repoFullName,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<DependabotPrSummary>>(OpenPrs);
    }
}
