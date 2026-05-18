using FluentAssertions;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

// Tests that ManifestPath on InventoryItem is honoured by manifest editors when the
// FixApplier passes item down to Apply(). Exercises the path that was broken for
// monorepo subfolders: items with ManifestPath="packages/sub/package.json" must
// edit that file, not the root package.json.
public sealed class ManifestPathFixApplierTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "shield-manifestpath-applier-" + Guid.NewGuid().ToString("n")
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

    [Fact]
    public void NpmEditor_edits_subfolder_package_json_when_ManifestPath_is_set()
    {
        string root = TempDir();
        try
        {
            string subDir = Path.Combine(root, "packages", "sub");
            Directory.CreateDirectory(subDir);

            string subManifest = Path.Combine(subDir, "package.json");
            File.WriteAllText(
                subManifest,
                "{\n  \"name\": \"sub\",\n  \"dependencies\": {\n    \"lodash\": \"^4.17.20\"\n  }\n}\n"
            );

            string rootManifest = Path.Combine(root, "package.json");
            File.WriteAllText(
                rootManifest,
                "{\n  \"name\": \"root\",\n  \"dependencies\": {\n    \"lodash\": \"^4.17.20\"\n  }\n}\n"
            );

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = true,
                ManifestPath = "packages/sub/package.json",
            };

            ManifestEditOutcome outcome = editor.Apply(root, item, "4.17.21");

            outcome.UnsupportedReason.Should().BeNull();
            outcome
                .ChangedFiles.Should()
                .ContainSingle(file => file.Contains(Path.Combine("packages", "sub")));

            string subAfter = File.ReadAllText(subManifest);
            subAfter.Should().Contain("\"lodash\": \"^4.17.21\"");

            string rootAfter = File.ReadAllText(rootManifest);
            rootAfter.Should().Contain("\"lodash\": \"^4.17.20\"");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void NpmEditor_falls_back_to_root_package_json_when_ManifestPath_is_null()
    {
        string root = TempDir();
        try
        {
            string manifest = Path.Combine(root, "package.json");
            File.WriteAllText(
                manifest,
                "{\n  \"dependencies\": {\n    \"express\": \"^4.18.0\"\n  }\n}\n"
            );

            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "express",
                Version = "4.18.0",
                IsDirect = true,
                ManifestPath = null,
            };

            ManifestEditOutcome outcome = editor.Apply(root, item, "4.19.0");

            outcome.UnsupportedReason.Should().BeNull();
            outcome.ChangedFiles.Should().ContainSingle(file => file.EndsWith("package.json"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void NpmEditor_returns_clear_error_when_ManifestPath_file_does_not_exist()
    {
        string root = TempDir();
        try
        {
            NpmManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = true,
                ManifestPath = "packages/missing/package.json",
            };

            ManifestEditOutcome outcome = editor.Apply(root, item, "4.17.21");

            outcome.UnsupportedReason.Should().NotBeNull();
            outcome.ChangedFiles.Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ComposerEditor_edits_subfolder_composer_json_when_ManifestPath_is_set()
    {
        string root = TempDir();
        try
        {
            string subDir = Path.Combine(root, "packages", "api");
            Directory.CreateDirectory(subDir);

            string subManifest = Path.Combine(subDir, "composer.json");
            File.WriteAllText(subManifest, "{\"require\":{\"monolog/monolog\":\"^2.0\"}}");

            string rootManifest = Path.Combine(root, "composer.json");
            File.WriteAllText(rootManifest, "{\"require\":{\"monolog/monolog\":\"^2.0\"}}");

            ComposerManifestEditor editor = new();
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Composer,
                Name = "monolog/monolog",
                Version = "2.0.0",
                IsDirect = true,
                ManifestPath = "packages/api/composer.json",
            };

            ManifestEditOutcome outcome = editor.Apply(root, item, "3.0.0");

            outcome.UnsupportedReason.Should().BeNull();
            outcome
                .ChangedFiles.Should()
                .ContainSingle(file => file.Contains(Path.Combine("packages", "api")));

            string subAfter = File.ReadAllText(subManifest);
            subAfter.Should().Contain("^3.0.0");

            string rootAfter = File.ReadAllText(rootManifest);
            rootAfter.Should().Contain("^2.0");
        }
        finally
        {
            Cleanup(root);
        }
    }
}
