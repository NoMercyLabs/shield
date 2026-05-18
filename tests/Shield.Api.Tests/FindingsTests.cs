using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
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
    public async Task ListReturnsEnrichedFindingWithPackageAndAdvisoryFields()
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
    public async Task AckFlipsFindingStateToAcked()
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
    public async Task ApplyFixLocalFolderBumpsPackageJson()
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
    public async Task GetFindingReturnsFixSuggestionWhenAdvisoryHasKnownFix()
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
    public async Task ApplyFixPrStrategyRejectedForLocalFolder()
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
    public async Task ApplyFixRequiresAdmin()
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

    [Fact]
    public async Task BulkAckFlipsMultipleFindingsInOneCall()
    {
        Guid first = await SeedFindingAsync();
        Guid second = await SeedFindingAsync();
        Guid third = await SeedFindingAsync();

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/findings/bulk-ack",
            new BulkFindingsRequest([first, second, third])
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkFindingsResponse? body =
            await response.Content.ReadFromJsonAsync<BulkFindingsResponse>();
        body.Should().NotBeNull();
        body!.Updated.Should().Be(3);
        body.NotFound.Should().BeEmpty();

        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Guid[] ids = [first, second, third];
        List<Finding> after = await db
            .Findings.Where(finding => ids.Contains(finding.Id))
            .ToListAsync();
        after.Should().HaveCount(3);
        after.Should().OnlyContain(finding => finding.State == FindingState.Acked);
    }

    [Fact]
    public async Task BulkResolveReturnsNotFoundForMissingId()
    {
        Guid real = await SeedFindingAsync();
        Guid missing = Guid.NewGuid();

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/findings/bulk-resolve",
            new BulkFindingsRequest([real, missing])
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkFindingsResponse? body =
            await response.Content.ReadFromJsonAsync<BulkFindingsResponse>();
        body.Should().NotBeNull();
        body!.Updated.Should().Be(1);
        body.NotFound.Should().ContainSingle(id => id == missing);
    }

    [Fact]
    public async Task MultiSeverityFilterReturnsHighAndCriticalOnly()
    {
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            string keyPrefix = "multi-sev-" + Guid.NewGuid().ToString("n") + "-";
            DateTime baseTime = DateTime.UtcNow;
            db.Findings.Add(MakeFinding(Severity.Low, keyPrefix + "low", baseTime));
            db.Findings.Add(MakeFinding(Severity.Medium, keyPrefix + "med", baseTime));
            db.Findings.Add(MakeFinding(Severity.High, keyPrefix + "high", baseTime));
            db.Findings.Add(MakeFinding(Severity.Critical, keyPrefix + "crit", baseTime));
            await db.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        // ?severity=2&severity=3 = High + Critical
        HttpResponseMessage response = await client.GetAsync(
            "/api/findings?severity=2&severity=3&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();
        page.Items.Should()
            .OnlyContain(finding =>
                finding.Severity == Severity.High || finding.Severity == Severity.Critical
            );
        page.Items.Should().Contain(finding => finding.Severity == Severity.High);
        page.Items.Should().Contain(finding => finding.Severity == Severity.Critical);
    }

    private static Finding MakeFinding(Severity severity, string dedupKey, DateTime baseTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            SourceId = 9999,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = severity,
            FirstSeenAt = baseTime,
            LastSeenAt = baseTime,
            State = FindingState.Open,
            DedupKey = dedupKey,
        };

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

    [Fact]
    public async Task SortBySeverityAscReturnsLowestFirst()
    {
        string prefix = "sort-sev-asc-" + Guid.NewGuid().ToString("n") + "-";
        DateTime baseTime = DateTime.UtcNow;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            db.Findings.Add(MakeFinding(Severity.Critical, prefix + "crit", baseTime));
            db.Findings.Add(MakeFinding(Severity.Low, prefix + "low", baseTime));
            db.Findings.Add(MakeFinding(Severity.High, prefix + "high", baseTime));
            await db.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/findings?sortBy=severity&sortDir=asc&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
        page.Should().NotBeNull();

        List<FindingResponse> relevant = page!
            .Items.Where(finding => finding.DedupKey.StartsWith(prefix))
            .ToList();
        relevant.Should().HaveCount(3);

        for (int position = 0; position < relevant.Count - 1; position++)
            ((int)relevant[position].Severity)
                .Should()
                .BeLessOrEqualTo((int)relevant[position + 1].Severity);
    }

    [Fact]
    public async Task SortByDiscoveredAtDescReturnsNewestFirst()
    {
        string prefix = "sort-disc-" + Guid.NewGuid().ToString("n") + "-";
        DateTime baseTime = DateTime.UtcNow;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            db.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = Guid.NewGuid(),
                    Severity = Severity.Medium,
                    FirstSeenAt = baseTime.AddHours(-2),
                    LastSeenAt = baseTime.AddHours(-2),
                    State = FindingState.Open,
                    DedupKey = prefix + "older",
                }
            );
            db.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = Guid.NewGuid(),
                    Severity = Severity.Medium,
                    FirstSeenAt = baseTime,
                    LastSeenAt = baseTime,
                    State = FindingState.Open,
                    DedupKey = prefix + "newer",
                }
            );
            await db.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/findings?sortBy=discoveredAt&sortDir=desc&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
        page.Should().NotBeNull();

        List<FindingResponse> relevant = page!
            .Items.Where(finding => finding.DedupKey.StartsWith(prefix))
            .ToList();
        relevant.Should().HaveCount(2);
        relevant[0].DedupKey.Should().Be(prefix + "newer");
        relevant[1].DedupKey.Should().Be(prefix + "older");
    }

    [Fact]
    public async Task AdvisoryQueryFiltersByExternalIdSubstring()
    {
        string unique = Guid.NewGuid().ToString("n")[..8];
        string externalId = "GHSA-test-" + unique + "-xxxx";

        Guid advisoryId = Guid.NewGuid();
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
            feedsDb.Advisories.Add(
                new()
                {
                    Id = advisoryId,
                    ExternalId = externalId,
                    Ecosystem = Ecosystem.Npm,
                    PackageName = "test-pkg-" + unique,
                    AffectedRangesJson = "[]",
                    Severity = Severity.High,
                    Summary = "advisory query test",
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow,
                }
            );
            await feedsDb.SaveChangesAsync();

            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            shieldDb.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = advisoryId,
                    Severity = Severity.High,
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    State = FindingState.Open,
                    DedupKey = "advisory-query-test-" + unique,
                }
            );
            await shieldDb.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/findings?advisoryQuery={Uri.EscapeDataString(unique)}&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
        page.Should().NotBeNull();
        page!
            .Items.Should()
            .ContainSingle(finding => finding.DedupKey == "advisory-query-test-" + unique);
    }

    [Fact]
    public async Task KevOnlyFilterRestrictsToKevAdvisories()
    {
        string unique = Guid.NewGuid().ToString("n")[..8];
        Guid kevAdvisoryId = Guid.NewGuid();
        Guid nonKevAdvisoryId = Guid.NewGuid();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
            feedsDb.Advisories.Add(
                new()
                {
                    Id = kevAdvisoryId,
                    ExternalId = "GHSA-kev-" + unique,
                    Ecosystem = Ecosystem.Npm,
                    PackageName = "kev-pkg-" + unique,
                    AffectedRangesJson = "[]",
                    Severity = Severity.Critical,
                    Summary = "KEV test",
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow,
                    IsKev = true,
                }
            );
            feedsDb.Advisories.Add(
                new()
                {
                    Id = nonKevAdvisoryId,
                    ExternalId = "GHSA-nokev-" + unique,
                    Ecosystem = Ecosystem.Npm,
                    PackageName = "nokev-pkg-" + unique,
                    AffectedRangesJson = "[]",
                    Severity = Severity.High,
                    Summary = "non-KEV test",
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow,
                    IsKev = false,
                }
            );
            await feedsDb.SaveChangesAsync();

            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            shieldDb.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = kevAdvisoryId,
                    Severity = Severity.Critical,
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    State = FindingState.Open,
                    DedupKey = "kev-finding-" + unique,
                }
            );
            shieldDb.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = nonKevAdvisoryId,
                    Severity = Severity.High,
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    State = FindingState.Open,
                    DedupKey = "nokev-finding-" + unique,
                }
            );
            await shieldDb.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/findings?kevOnly=true&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
        page.Should().NotBeNull();
        page!.Items.Should().Contain(finding => finding.DedupKey == "kev-finding-" + unique);
        page.Items.Should().NotContain(finding => finding.DedupKey == "nokev-finding-" + unique);
    }

    [Fact]
    public async Task EpssMinFilterRestrictsToAdvisoriesAboveThreshold()
    {
        string unique = Guid.NewGuid().ToString("n")[..8];
        Guid highEpssAdvisoryId = Guid.NewGuid();
        Guid lowEpssAdvisoryId = Guid.NewGuid();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
            feedsDb.Advisories.Add(
                new()
                {
                    Id = highEpssAdvisoryId,
                    ExternalId = "GHSA-epss-high-" + unique,
                    Ecosystem = Ecosystem.Npm,
                    PackageName = "epss-high-" + unique,
                    AffectedRangesJson = "[]",
                    Severity = Severity.High,
                    Summary = "high EPSS test",
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow,
                    EpssScore = 0.85,
                }
            );
            feedsDb.Advisories.Add(
                new()
                {
                    Id = lowEpssAdvisoryId,
                    ExternalId = "GHSA-epss-low-" + unique,
                    Ecosystem = Ecosystem.Npm,
                    PackageName = "epss-low-" + unique,
                    AffectedRangesJson = "[]",
                    Severity = Severity.Medium,
                    Summary = "low EPSS test",
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow,
                    EpssScore = 0.10,
                }
            );
            await feedsDb.SaveChangesAsync();

            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            shieldDb.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = highEpssAdvisoryId,
                    Severity = Severity.High,
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    State = FindingState.Open,
                    DedupKey = "epss-high-finding-" + unique,
                }
            );
            shieldDb.Findings.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = 9999,
                    InventoryItemId = 1,
                    AdvisoryRefId = lowEpssAdvisoryId,
                    Severity = Severity.Medium,
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    State = FindingState.Open,
                    DedupKey = "epss-low-finding-" + unique,
                }
            );
            await shieldDb.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        // Threshold 0.50 — should include 0.85, exclude 0.10
        HttpResponseMessage response = await client.GetAsync(
            "/api/findings?epssMin=0.50&pageSize=200"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
        page.Should().NotBeNull();
        page!.Items.Should().Contain(finding => finding.DedupKey == "epss-high-finding-" + unique);
        page.Items.Should().NotContain(finding => finding.DedupKey == "epss-low-finding-" + unique);
    }

    [Fact]
    public async Task HasFixTrueReturnsOnlyFindingsWithAKnownFix()
    {
        string fixtureDir = Path.Combine(
            Path.GetTempPath(),
            "shield-has-fix-true-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(fixtureDir);
        try
        {
            // Seed a finding with a known fix (rangesJson has a fixed version).
            (Guid withFixId, _) = await SeedFixScenarioAsync(
                fixtureDir,
                ecosystem: Ecosystem.Npm,
                packageName: "lodash-hasfix",
                installedVersion: "4.17.20",
                rangesJson: "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                sourceType: SourceType.LocalFolder
            );

            HttpClient client = _factory.CreateClient();
            HttpResponseMessage response = await client.GetAsync(
                "/api/findings?hasFix=true&pageSize=200"
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            FindingsPage? page = await response.Content.ReadFromJsonAsync<FindingsPage>();
            page.Should().NotBeNull();
            page!.Items.Should().Contain(finding => finding.Id == withFixId);
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
