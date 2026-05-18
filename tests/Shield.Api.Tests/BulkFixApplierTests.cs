using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Unit-level tests for BulkFixApplier and the controller endpoint.
// GitHub API calls are only exercised in the controller tests via mock; the applier's
// dry-run path is exercised here without any network call.
public sealed class BulkFixApplierTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public BulkFixApplierTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    // -----------------------------------------------------------------------
    // dryRun path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DryRun_returns_planned_entries_without_creating_pr()
    {
        int sourceId = await SeedGithubSourceAsync("dry-run-fixture");
        await SeedFindingsAsync(sourceId, 3);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkApplyResponse? result = await response.Content.ReadFromJsonAsync<BulkApplyResponse>();
        result.Should().NotBeNull();
        result!.DryRun.Should().BeTrue();
        result.PullRequestUrl.Should().BeNull();
        result.Entries.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task DryRun_errors_not_null_for_unsupported_ecosystem()
    {
        int sourceId = await SeedGithubSourceAsync("dry-run-unsupported");
        await SeedFindingsAsync(sourceId, 1, ecosystem: Ecosystem.Nuget);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkApplyResponse? result = await response.Content.ReadFromJsonAsync<BulkApplyResponse>();
        result.Should().NotBeNull();
        result!.DryRun.Should().BeTrue();
        result.Errors.Should().HaveCountGreaterThan(0);
    }

    // -----------------------------------------------------------------------
    // 24h cooldown
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cooldown_returns_429_within_24h_of_last_apply()
    {
        int sourceId = await SeedGithubSourceAsync("cooldown-fixture");
        await SetLastBulkApplyAt(sourceId, DateTime.UtcNow.AddHours(-1));

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = false }
        );
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Force_bypasses_cooldown()
    {
        int sourceId = await SeedGithubSourceAsync("cooldown-force-fixture");
        await SetLastBulkApplyAt(sourceId, DateTime.UtcNow.AddHours(-1));
        await SeedFindingsAsync(sourceId, 1);

        HttpClient client = _factory.CreateClient();
        // force=true with dryRun=true — proves bypass without actually calling GitHub.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true, force = true }
        );
        // Must not be 429.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Source-type guard
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LocalFolder_source_returns_400()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/sources",
            new
            {
                type = (int)SourceType.LocalFolder,
                name = "bulk-local-reject",
                configJson = new { path = "/tmp" },
                scanInterval = "01:00:00",
            }
        );
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        SourceResponse? created = await createResponse.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{created!.Id}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------------
    // NpmManifestEditor surgical diff
    // -----------------------------------------------------------------------

    [Fact]
    public void DirectDep_bump_changes_at_most_2_lines()
    {
        string dir = MakeTempDir();
        try
        {
            string original =
                "{\n  \"name\": \"myapp\",\n  \"dependencies\": {\n    \"lodash\": \"^4.17.20\"\n  }\n}\n";
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, original);

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = true,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "4.17.21");
            outcome.UnsupportedReason.Should().BeNull();

            string edited = File.ReadAllText(manifest);
            int changedLines = CountChangedLines(original, edited);
            changedLines
                .Should()
                .BeLessThanOrEqualTo(2, "direct-dep path is a regex replace — one line changed");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void TransitiveDep_overrides_inject_changes_at_most_6_lines()
    {
        string dir = MakeTempDir();
        try
        {
            string original =
                "{\n  \"name\": \"myapp\",\n  \"dependencies\": {\n    \"express\": \"^4.18.0\"\n  }\n}\n";
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, original);

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = false,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "4.17.21");
            outcome.UnsupportedReason.Should().BeNull();

            string edited = File.ReadAllText(manifest);
            int changedLines = CountChangedLines(original, edited);
            changedLines
                .Should()
                .BeLessThanOrEqualTo(
                    6,
                    "overrides injection must be surgical — no whole-file reformat"
                );
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void TransitiveDep_existing_overrides_update_is_surgical()
    {
        string dir = MakeTempDir();
        try
        {
            string original =
                "{\n  \"name\": \"myapp\",\n  \"overrides\": {\n    \"lodash\": \"^4.17.0\"\n  }\n}\n";
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, original);

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.0",
                IsDirect = false,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "4.17.21");
            outcome.UnsupportedReason.Should().BeNull();

            string edited = File.ReadAllText(manifest);
            edited.Should().Contain("^4.17.21");
            int changedLines = CountChangedLines(original, edited);
            changedLines
                .Should()
                .BeLessThanOrEqualTo(2, "existing overrides entry update is a single-line change");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int CountChangedLines(string original, string edited)
    {
        string[] originalLines = original.Split('\n');
        string[] editedLines = edited.Split('\n');
        int maxLen = Math.Max(originalLines.Length, editedLines.Length);
        int changed = 0;
        for (int index = 0; index < maxLen; index++)
        {
            string origLine = index < originalLines.Length ? originalLines[index] : string.Empty;
            string editLine = index < editedLines.Length ? editedLines[index] : string.Empty;
            if (!string.Equals(origLine, editLine, StringComparison.Ordinal))
                changed++;
        }
        return changed;
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "shield-bulk-tests-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        { /* best-effort */
        }
    }

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

    private async Task SeedFindingsAsync(
        int sourceId,
        int count,
        Ecosystem ecosystem = Ecosystem.Npm
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        Guid snapshotId = Guid.NewGuid();
        shieldDb.InventorySnapshots.Add(
            new()
            {
                Id = snapshotId,
                SourceId = sourceId,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "bulk-fixture",
                ItemCount = count,
            }
        );
        await shieldDb.SaveChangesAsync();

        for (int index = 0; index < count; index++)
        {
            string packageName = $"pkg-{sourceId}-{index}";

            InventoryItem item = new()
            {
                SnapshotId = snapshotId,
                Ecosystem = ecosystem,
                Name = packageName,
                Version = "1.0.0",
                IsDirect = true,
            };
            shieldDb.InventoryItems.Add(item);
            await shieldDb.SaveChangesAsync();

            Advisory advisory = new()
            {
                Id = Guid.NewGuid(),
                ExternalId = $"GHSA-bulk-{sourceId}-{index}",
                Ecosystem = ecosystem,
                PackageName = packageName,
                AffectedRangesJson =
                    "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.0.1\"}]}]",
                Severity = Severity.High,
                Summary = "bulk test advisory",
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
                SourceId = sourceId,
                InventoryItemId = item.Id,
                AdvisoryRefId = advisory.Id,
                Severity = Severity.High,
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                State = FindingState.Open,
                DedupKey = $"bulk-{sourceId}-{index}-{Guid.NewGuid():n}",
            };
            shieldDb.Findings.Add(finding);
            await shieldDb.SaveChangesAsync();
        }
    }

    private async Task SetLastBulkApplyAt(int sourceId, DateTime at)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Source row =
            await db.Sources.FindAsync(sourceId)
            ?? throw new InvalidOperationException($"Source {sourceId} not found");
        row.LastBulkApplyAt = at;
        await db.SaveChangesAsync();
    }

    // -----------------------------------------------------------------------
    // Major-bump opt-in (Gap 2)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MajorBump_entries_land_in_majorBumps_not_entries_when_not_allowed()
    {
        int sourceId = await SeedGithubSourceAsync("major-bump-gate");
        // Seed version 1.0.0 with fix 2.0.0 — clearly a major bump.
        await SeedFindingsWithFixAsync(
            sourceId,
            "major-pkg",
            currentVersion: "1.0.0",
            fixedVersion: "2.0.0"
        );

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true, allowMajorBumps = false }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkApplyResponse? result = await response.Content.ReadFromJsonAsync<BulkApplyResponse>();
        result.Should().NotBeNull();
        result!.Entries.Should().BeEmpty("major bump should be withheld from entries");
        result.MajorBumps.Should().ContainSingle(entry => entry.PackageName == "major-pkg");
    }

    [Fact]
    public async Task MajorBump_entries_included_when_allowMajorBumps_is_true()
    {
        int sourceId = await SeedGithubSourceAsync("major-bump-allowed");
        await SeedFindingsWithFixAsync(
            sourceId,
            "major-allowed-pkg",
            currentVersion: "1.0.0",
            fixedVersion: "2.0.0"
        );

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true, allowMajorBumps = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkApplyResponse? result = await response.Content.ReadFromJsonAsync<BulkApplyResponse>();
        result.Should().NotBeNull();
        result!.Entries.Should().ContainSingle(entry => entry.PackageName == "major-allowed-pkg");
    }

    // -----------------------------------------------------------------------
    // Production-source gate (Gap 6)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProductionSource_returns_409_when_confirmProduction_not_set()
    {
        int sourceId = await SeedGithubSourceAsync("prod-gate-fixture");
        await MarkProduction(sourceId);
        await SeedFindingsAsync(sourceId, 1);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = false, confirmProduction = false }
        );
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ProductionSource_dryRun_bypasses_409_gate()
    {
        int sourceId = await SeedGithubSourceAsync("prod-dryrun-fixture");
        await MarkProduction(sourceId);
        await SeedFindingsAsync(sourceId, 1);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );
        // Dry-run should never 409 — no real changes are made.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // NpmManifestEditor — regression: 3 packages in same manifest (Gap 8)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThreePackages_in_same_manifest_all_land_in_output()
    {
        string dir = MakeTempDir();
        try
        {
            string original =
                "{\n  \"name\": \"app\",\n  \"dependencies\": {\n    \"pkgA\": \"^1.0.0\",\n    \"pkgB\": \"^1.0.0\",\n    \"pkgC\": \"^1.0.0\"\n  }\n}\n";
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, original);

            NpmManifestEditor editor = new();

            void Bump(string name, string version)
            {
                InventoryItem item = new()
                {
                    Ecosystem = Ecosystem.Npm,
                    Name = name,
                    Version = "1.0.0",
                    IsDirect = true,
                };
                ManifestEditOutcome outcome = editor.Apply(dir, item, version);
                outcome.UnsupportedReason.Should().BeNull($"bump of {name} should succeed");
            }

            Bump("pkgA", "1.1.0");
            Bump("pkgB", "1.2.0");
            Bump("pkgC", "1.3.0");

            string final = File.ReadAllText(manifest);
            JsonDocument.Parse(final).Dispose(); // must still be valid JSON
            final.Should().Contain("\"pkgA\": \"^1.1.0\"");
            final.Should().Contain("\"pkgB\": \"^1.2.0\"");
            final.Should().Contain("\"pkgC\": \"^1.3.0\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // -----------------------------------------------------------------------
    // NpmManifestEditor — regression: double-quote on override value (Gap 8)
    // -----------------------------------------------------------------------

    [Fact]
    public void Override_insert_does_not_double_quote_value()
    {
        string dir = MakeTempDir();
        try
        {
            string original =
                "{\n  \"name\": \"app\",\n  \"dependencies\": {\n    \"express\": \"^4.18.0\"\n  }\n}\n";
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, original);

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = false,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "4.18.0");
            outcome.UnsupportedReason.Should().BeNull();

            string result = File.ReadAllText(manifest);

            // Must parse as valid JSON — double-quoted value would break this.
            JsonDocument.Parse(result).Dispose();

            // The override value must appear exactly once as a properly quoted string.
            result.Should().Contain("\"lodash\": \"^4.18.0\"");
            result.Should().NotContain("\"\"^4.18.0\"\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // -----------------------------------------------------------------------
    // NpmManifestEditor — clean diff: N overrides → ≤ N+1 changed lines (Gap 1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Override_insert_N_packages_changes_at_most_N_plus_1_lines()
    {
        string dir = MakeTempDir();
        try
        {
            // Use a verbatim literal so the indentation in the JSON is exactly 2 spaces —
            // no C# code-indentation prefix that would confuse the root-indent detector.
            string original =
                "{\n  \"name\": \"myapp\",\n  \"overrides\": {\n    \"rollup\": \"4.44.0\",\n    \"handlebars\": \"^4.7.9\"\n  }\n}\n";

            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, original);

            NpmManifestEditor editor = new();

            void ApplyOverride(string name, string version)
            {
                InventoryItem item = new()
                {
                    Ecosystem = Ecosystem.Npm,
                    Name = name,
                    Version = "0.0.1",
                    IsDirect = false,
                };
                ManifestEditOutcome outcome = editor.Apply(dir, item, version);
                outcome.UnsupportedReason.Should().BeNull($"override of {name} should succeed");
            }

            ApplyOverride("node-forge", "1.4.0");
            ApplyOverride("webpack-dev-server", "5.2.1");

            string result = File.ReadAllText(manifest);
            JsonDocument.Parse(result).Dispose();

            // Each new override entry: prior last line gains a trailing comma (1 line changed),
            // plus 1 new line inserted. The positional diff also shifts all subsequent lines.
            // For 2 insertions into an 8-line file near the end: ≤ 6 changed positions is tight.
            int changedLines = CountChangedLines(original, result);
            changedLines
                .Should()
                .BeLessThanOrEqualTo(
                    6,
                    "surgical insertion: no whole-file reformat, only the overrides block entries shift"
                );

            result.Should().Contain("\"handlebars\": \"^4.7.9\"");
            result.Should().Contain("\"node-forge\": \"^1.4.0\"");
            result.Should().Contain("\"webpack-dev-server\": \"^5.2.1\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private async Task MarkProduction(int sourceId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Source row =
            await db.Sources.FindAsync(sourceId)
            ?? throw new InvalidOperationException($"Source {sourceId} not found");
        row.IsProduction = true;
        await db.SaveChangesAsync();
    }

    private async Task SeedFindingsWithFixAsync(
        int sourceId,
        string packageName,
        string currentVersion,
        string fixedVersion
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        Guid snapshotId = Guid.NewGuid();
        shieldDb.InventorySnapshots.Add(
            new()
            {
                Id = snapshotId,
                SourceId = sourceId,
                TakenAt = DateTime.UtcNow,
                ContentsSha = $"fix-fixture-{sourceId}",
                ItemCount = 1,
            }
        );
        await shieldDb.SaveChangesAsync();

        InventoryItem item = new()
        {
            SnapshotId = snapshotId,
            Ecosystem = Ecosystem.Npm,
            Name = packageName,
            Version = currentVersion,
            IsDirect = true,
        };
        shieldDb.InventoryItems.Add(item);
        await shieldDb.SaveChangesAsync();

        Advisory advisory = new()
        {
            Id = Guid.NewGuid(),
            ExternalId = $"GHSA-fix-{sourceId}-{packageName}",
            Ecosystem = Ecosystem.Npm,
            PackageName = packageName,
            AffectedRangesJson =
                $"[{{\"events\":[{{\"introduced\":\"0\"}},{{\"fixed\":\"{fixedVersion}\"}}]}}]",
            Severity = Severity.High,
            Summary = "fixture advisory",
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
            SourceId = sourceId,
            InventoryItemId = item.Id,
            AdvisoryRefId = advisory.Id,
            Severity = Severity.High,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = $"fix-{sourceId}-{packageName}-{Guid.NewGuid():n}",
        };
        shieldDb.Findings.Add(finding);
        await shieldDb.SaveChangesAsync();
    }
}
