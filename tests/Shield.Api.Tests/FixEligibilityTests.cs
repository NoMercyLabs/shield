using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Verifies prEligibility / autoEligibility fields on FixSuggestionResponse and confirms
// the NpmManifestEditor overrides path for transitive deps.
public sealed class FixEligibilityTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public FixEligibilityTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    // ------------------------------------------------------------------
    // Eligibility via the detail endpoint
    // ------------------------------------------------------------------

    [Fact]
    public async Task LocalFolderNpmFindingIsAutoEligibleNotPrEligible()
    {
        string dir = TempDir();
        try
        {
            (Guid id, _) = await SeedAsync(dir, Ecosystem.Npm, SourceType.LocalFolder);
            FindingDetailResponse detail = await GetDetailAsync(id);

            detail.FixSuggestion.Should().NotBeNull();
            detail.FixSuggestion!.AutoEligibility.Eligible.Should().BeTrue();
            detail.FixSuggestion.PrEligibility.Eligible.Should().BeFalse();
            detail.FixSuggestion.PrEligibility.Reason.Should().Contain("GithubRepo");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GithubRepoNpmFindingIsPrEligibleNotAutoEligible()
    {
        string dir = TempDir();
        try
        {
            (Guid id, _) = await SeedAsync(dir, Ecosystem.Npm, SourceType.GithubRepo);
            FindingDetailResponse detail = await GetDetailAsync(id);

            detail.FixSuggestion.Should().NotBeNull();
            detail.FixSuggestion!.PrEligibility.Eligible.Should().BeTrue();
            detail.FixSuggestion.AutoEligibility.Eligible.Should().BeFalse();
            detail.FixSuggestion.AutoEligibility.Reason.Should().Contain("LocalFolder");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task LocalFolderPythonFindingIsAutoNotEligibleNoEditor()
    {
        string dir = TempDir();
        try
        {
            (Guid id, _) = await SeedAsync(dir, Ecosystem.Python, SourceType.LocalFolder);
            FindingDetailResponse detail = await GetDetailAsync(id);

            detail.FixSuggestion.Should().NotBeNull();
            // Python has an editor registered (PythonManifestEditor) but it always
            // returns UnsupportedReason at apply-time. For eligibility: editor exists,
            // path is set, source is LocalFolder → eligible=true. The UnsupportedReason
            // comes back only if the user actually clicks apply.
            detail.FixSuggestion!.AutoEligibility.Eligible.Should().BeTrue();
            detail.FixSuggestion.PrEligibility.Eligible.Should().BeFalse();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task GithubRepoComposerFindingIsPrEligible()
    {
        string dir = TempDir();
        try
        {
            (Guid id, _) = await SeedAsync(dir, Ecosystem.Composer, SourceType.GithubRepo);
            FindingDetailResponse detail = await GetDetailAsync(id);

            detail.FixSuggestion.Should().NotBeNull();
            detail.FixSuggestion!.PrEligibility.Eligible.Should().BeTrue();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // ------------------------------------------------------------------
    // NpmManifestEditor overrides path (unit level)
    // ------------------------------------------------------------------

    [Fact]
    public void NpmManifestEditorInjectsOverridesForTransitiveDep()
    {
        string dir = TempDir();
        try
        {
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(
                manifest,
                "{\n  \"name\": \"myapp\",\n  \"dependencies\": {\n    \"express\": \"^4.18.0\"\n  }\n}\n"
            );

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
            outcome.ChangedFiles.Should().ContainSingle(file => file.EndsWith("package.json"));
            outcome.FollowUpCommand.Should().Be("npm install");

            string written = File.ReadAllText(manifest);
            written.Should().Contain("\"overrides\"");
            written.Should().Contain("\"lodash\"");
            written.Should().Contain("\"^4.17.21\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void NpmManifestEditorUpdatesExistingOverridesEntry()
    {
        string dir = TempDir();
        try
        {
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(
                manifest,
                "{\n  \"name\": \"myapp\",\n  \"overrides\": {\n    \"lodash\": \"^4.17.0\"\n  }\n}\n"
            );

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
            string written = File.ReadAllText(manifest);
            written.Should().Contain("\"^4.17.21\"");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void NpmManifestEditorDirectDepStillUsesDirectPath()
    {
        string dir = TempDir();
        try
        {
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(
                manifest,
                "{\n  \"dependencies\": {\n    \"lodash\": \"^4.17.20\"\n  }\n}\n"
            );

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
            string written = File.ReadAllText(manifest);
            written.Should().Contain("\"lodash\": \"^4.17.21\"");
            written.Should().NotContain("overrides");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void NpmManifestEditorTransitiveDepNotFoundReturnsUnsupportedWhenMalformedJson()
    {
        string dir = TempDir();
        try
        {
            string manifest = Path.Combine(dir, "package.json");
            File.WriteAllText(manifest, "{ NOT VALID JSON !! }");

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = false,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "4.17.21");

            outcome.UnsupportedReason.Should().NotBeNull();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // ------------------------------------------------------------------
    // ComposerManifestEditor transitive returns clear reason
    // ------------------------------------------------------------------

    [Fact]
    public void ComposerManifestEditorTransitiveDepReturnsUnsupportedReason()
    {
        string dir = TempDir();
        try
        {
            string manifest = Path.Combine(dir, "composer.json");
            File.WriteAllText(manifest, "{\"require\":{\"monolog/monolog\":\"^3.0\"}}");

            ComposerManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Composer,
                Name = "psr/log",
                Version = "3.0.0",
                IsDirect = false,
            };

            ManifestEditOutcome outcome = editor.Apply(dir, item, "3.0.1");

            outcome.UnsupportedReason.Should().NotBeNull();
            outcome.UnsupportedReason.Should().Contain("Transitive");
            outcome.ChangedFiles.Should().BeEmpty();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string TempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "shield-eligibility-" + Guid.NewGuid().ToString("n")
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

    private async Task<FindingDetailResponse> GetDetailAsync(Guid id)
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync($"/api/findings/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FindingDetailResponse? detail =
            await response.Content.ReadFromJsonAsync<FindingDetailResponse>();
        detail.Should().NotBeNull();
        return detail!;
    }

    private async Task<(Guid FindingId, int SourceId)> SeedAsync(
        string dir,
        Ecosystem ecosystem,
        SourceType sourceType
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        string configJson =
            sourceType == SourceType.LocalFolder
                ? JsonSerializer.Serialize(new { path = dir })
                : JsonSerializer.Serialize(new { owner = "shield-test", repo = "fixture" });

        Source source = new()
        {
            Type = sourceType,
            Name = "eligibility-fixture-" + Guid.NewGuid().ToString("n"),
            ConfigJson = configJson,
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        shieldDb.Sources.Add(source);
        await shieldDb.SaveChangesAsync();

        Guid snapshotId = Guid.NewGuid();
        shieldDb.InventorySnapshots.Add(
            new()
            {
                Id = snapshotId,
                SourceId = source.Id,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "fixture",
                ItemCount = 1,
            }
        );
        InventoryItem item = new()
        {
            SnapshotId = snapshotId,
            Ecosystem = ecosystem,
            Name = "test-pkg",
            Version = "1.0.0",
            IsDirect = true,
        };
        shieldDb.InventoryItems.Add(item);
        await shieldDb.SaveChangesAsync();

        Advisory advisory = new()
        {
            Id = Guid.NewGuid(),
            ExternalId = "TEST-ELIG-" + Guid.NewGuid().ToString("n"),
            Ecosystem = ecosystem,
            PackageName = "test-pkg",
            AffectedRangesJson = "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.0.1\"}]}]",
            Severity = Severity.High,
            Summary = "eligibility test advisory",
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
            Severity = Severity.High,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "elig-" + Guid.NewGuid().ToString("n"),
        };
        shieldDb.Findings.Add(finding);
        await shieldDb.SaveChangesAsync();
        return (finding.Id, source.Id);
    }
}
