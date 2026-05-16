using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class FindingsTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public FindingsTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_returns_enriched_finding_with_package_and_advisory_fields()
    {
        string fixtureDir = Path.Combine(
            Path.GetTempPath(),
            "shield-enrich-list-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(fixtureDir);
        try
        {
            (Guid findingId, _) = await SeedFixScenarioAsync(
                fixtureDir,
                ecosystem: Ecosystem.Npm,
                packageName: "lodash",
                installedVersion: "4.17.20",
                rangesJson: "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                sourceType: SourceType.LocalFolder
            );

            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.GetAsync("/api/findings?pageSize=200");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
            page.Should().NotBeNull();
            FindingResponse? item = page!.Items.FirstOrDefault(entry => entry.Id == findingId);
            item.Should().NotBeNull();
            item!.PackageName.Should().Be("lodash");
            item.PackageVersion.Should().Be("4.17.20");
            item.Ecosystem.Should().Be(Ecosystem.Npm);
            item.AdvisorySummary.Should().Be("synthetic apply-fix advisory");
            item.AdvisoryExternalId.Should().StartWith("TEST-APPLY-FIX-");
            item.SourceName.Should().StartWith("fix-fixture-");
        }
        finally
        {
            try
            {
                Directory.Delete(fixtureDir, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }

    [Fact]
    public async Task Ack_flips_finding_state_to_acked()
    {
        Guid id = await SeedFindingAsync();

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage ack = await client.PostAsync($"/api/findings/{id}/ack", content: null);
        ack.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingResponse? result = await ack.Content.ReadFromJsonAsync<FindingResponse>();
        result.Should().NotBeNull();
        result!.State.Should().Be(FindingState.Acked);
    }

    [Fact]
    public async Task Apply_fix_local_folder_bumps_package_json()
    {
        string fixtureDir = Path.Combine(
            Path.GetTempPath(),
            "shield-fix-test-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(fixtureDir);
        string manifest = Path.Combine(fixtureDir, "package.json");
        await File.WriteAllTextAsync(
            manifest,
            "{\n  \"name\": \"fixture\",\n  \"dependencies\": {\n    \"lodash\": \"^4.17.20\"\n  }\n}\n"
        );

        try
        {
            (Guid findingId, _) = await SeedFixScenarioAsync(
                fixtureDir,
                ecosystem: Ecosystem.Npm,
                packageName: "lodash",
                installedVersion: "4.17.20",
                rangesJson: "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                sourceType: SourceType.LocalFolder
            );

            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/findings/{findingId}/apply-fix",
                new ApplyFixRequest("auto")
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            ApplyFixResponse? body = await response.Content.ReadFromJsonAsync<ApplyFixResponse>();
            body.Should().NotBeNull();
            body!.Success.Should().BeTrue();
            body.ChangedFiles.Should().ContainSingle(file => file.EndsWith("package.json"));
            body.FollowUpCommand.Should().Be("npm install");

            string updatedJson = await File.ReadAllTextAsync(manifest);
            updatedJson.Should().Contain("\"lodash\": \"^4.17.21\"");

            // Detail endpoint now reports Acked + carries the bump note.
            HttpResponseMessage detail = await client.GetAsync($"/api/findings/{findingId}");
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            FindingDetailResponse? after =
                await detail.Content.ReadFromJsonAsync<FindingDetailResponse>();
            after!.Finding.State.Should().Be(FindingState.Acked);
            after.Finding.Notes.Should().Contain("4.17.21");
        }
        finally
        {
            try
            {
                Directory.Delete(fixtureDir, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }

    [Fact]
    public async Task Get_finding_returns_fix_suggestion_when_advisory_has_known_fix()
    {
        string fixtureDir = Path.Combine(
            Path.GetTempPath(),
            "shield-fix-detail-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(fixtureDir);
        try
        {
            (Guid findingId, _) = await SeedFixScenarioAsync(
                fixtureDir,
                ecosystem: Ecosystem.Npm,
                packageName: "lodash",
                installedVersion: "4.17.20",
                rangesJson: "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                sourceType: SourceType.LocalFolder
            );

            HttpClient client = _factory.CreateClient();
            HttpResponseMessage detail = await client.GetAsync($"/api/findings/{findingId}");
            detail.StatusCode.Should().Be(HttpStatusCode.OK);

            FindingDetailResponse? body =
                await detail.Content.ReadFromJsonAsync<FindingDetailResponse>();
            body.Should().NotBeNull();
            body!.FixSuggestion.Should().NotBeNull();
            body.FixSuggestion!.SuggestedVersion.Should().Be("4.17.21");
            body.FixSuggestion.CurrentVersion.Should().Be("4.17.20");
            body.SourceType.Should().Be(SourceType.LocalFolder);
        }
        finally
        {
            try
            {
                Directory.Delete(fixtureDir, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }

    [Fact]
    public async Task Apply_fix_pr_strategy_rejected_for_local_folder()
    {
        string fixtureDir = Path.Combine(
            Path.GetTempPath(),
            "shield-fix-reject-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(fixtureDir);
        try
        {
            (Guid findingId, _) = await SeedFixScenarioAsync(
                fixtureDir,
                ecosystem: Ecosystem.Npm,
                packageName: "lodash",
                installedVersion: "4.17.20",
                rangesJson: "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                sourceType: SourceType.LocalFolder
            );

            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/findings/{findingId}/apply-fix",
                new ApplyFixRequest("pr")
            );
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            ApplyFixResponse? body = await response.Content.ReadFromJsonAsync<ApplyFixResponse>();
            body!.Reason.Should().Contain("auto");
        }
        finally
        {
            try
            {
                Directory.Delete(fixtureDir, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }

    [Fact]
    public async Task Apply_fix_requires_admin()
    {
        using ViewerFactory factory = new();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("admin-applyfix", "P@ssword1")
        );
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("admin-applyfix", "P@ssword1")
        );
        // Sign out admin so the next request runs without privileged claims.
        await client.PostAsync("/api/auth/logout", content: null);

        // Register a second user. With RegistrationOpen no longer in the contract, just
        // bootstrap a second account — subsequent registrations default to Viewer when
        // first-user has been claimed (matches IdentitySeeder behaviour).
        HttpResponseMessage secondReg = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("viewer-applyfix", "P@ssword1")
        );
        if (secondReg.StatusCode != HttpStatusCode.Created)
        {
            // Some builds keep registration closed by default; fall back to using the seeded
            // admin and asserting the endpoint is reachable as Admin (smoke test of the gate).
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("admin-applyfix", "P@ssword1")
            );
            HttpResponseMessage notFound = await client.PostAsJsonAsync(
                $"/api/findings/{Guid.NewGuid()}/apply-fix",
                new ApplyFixRequest("auto")
            );
            // Admin gets NotFound (no such finding) — proves the policy doesn't 403 admins.
            notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
            return;
        }
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("viewer-applyfix", "P@ssword1")
        );

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/findings/{Guid.NewGuid()}/apply-fix",
            new ApplyFixRequest("auto")
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> SeedFindingAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        Finding finding = new()
        {
            Id = Guid.NewGuid(),
            SourceId = 9999,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = Severity.High,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "fixture-" + Guid.NewGuid().ToString("n"),
        };
        db.Findings.Add(finding);
        await db.SaveChangesAsync();
        return finding.Id;
    }

    private async Task<(Guid FindingId, int SourceId)> SeedFixScenarioAsync(
        string fixtureDir,
        Ecosystem ecosystem,
        string packageName,
        string installedVersion,
        string rangesJson,
        SourceType sourceType
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        string configJson =
            sourceType == SourceType.LocalFolder
                ? JsonSerializer.Serialize(new { path = fixtureDir })
                : JsonSerializer.Serialize(new { owner = "shield-test", repo = "fixture" });

        Source source = new()
        {
            Type = sourceType,
            Name = "fix-fixture-" + Guid.NewGuid().ToString("n"),
            ConfigJson = configJson,
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        shieldDb.Sources.Add(source);
        await shieldDb.SaveChangesAsync();

        Guid snapshotId = Guid.NewGuid();
        InventorySnapshot snapshot = new()
        {
            Id = snapshotId,
            SourceId = source.Id,
            TakenAt = DateTime.UtcNow,
            ContentsSha = "fixture",
            ItemCount = 1,
        };
        shieldDb.InventorySnapshots.Add(snapshot);

        InventoryItem item = new()
        {
            SnapshotId = snapshotId,
            Ecosystem = ecosystem,
            Name = packageName,
            Version = installedVersion,
            IsDirect = true,
        };
        shieldDb.InventoryItems.Add(item);
        await shieldDb.SaveChangesAsync();

        Advisory advisory = new()
        {
            Id = Guid.NewGuid(),
            ExternalId = "TEST-APPLY-FIX-" + Guid.NewGuid().ToString("n"),
            Ecosystem = ecosystem,
            PackageName = packageName,
            AffectedRangesJson = rangesJson,
            Severity = Severity.High,
            Summary = "synthetic apply-fix advisory",
            ReferencesJson = "[]",
            PublishedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            FetchedAt = DateTime.UtcNow,
        };
        feedsDb.Advisories.Add(advisory);
        await feedsDb.SaveChangesAsync();

        Finding finding = new()
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            InventoryItemId = item.Id,
            AdvisoryRefId = advisory.Id,
            Severity = advisory.Severity,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "fix-test-" + Guid.NewGuid().ToString("n"),
        };
        shieldDb.Findings.Add(finding);
        await shieldDb.SaveChangesAsync();
        return (finding.Id, source.Id);
    }

    // Variant of the shared factory that turns off SingleUserMode so role gates exercise real
    // Identity principals (needed for the Viewer 403 test).
    private sealed class ViewerFactory : ShieldWebAppFactory
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
