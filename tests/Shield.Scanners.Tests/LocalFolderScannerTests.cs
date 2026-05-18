using System.Text.Json;
using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Composer;
using Shield.Parsers.Dart;
using Shield.Parsers.Elixir;
using Shield.Parsers.Go;
using Shield.Parsers.Gradle;
using Shield.Parsers.Maven;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Parsers.Python;
using Shield.Parsers.Ruby;
using Shield.Parsers.Rust;
using Shield.Parsers.Swift;
using Shield.Parsers.Vcpkg;
using Shield.Scanners;
using Xunit;

namespace Shield.Scanners.Tests;

public class LocalFolderScannerTests
{
    private static ParserRegistry NewParserRegistry() =>
        new(
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new()
        );

    private static LocalFolderScanner NewScanner() => new(NewParserRegistry());

    private static string CopyFixtureTree()
    {
        string fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-scanner-tests-" + Guid.NewGuid().ToString("N")
        );
        CopyDirectory(fixtureRoot, tempRoot);
        return tempRoot;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            string dirName = Path.GetFileName(dir);
            // Materialise the node_modules + .git fixtures at runtime so the SDK's
            // default item exclusions don't strip them from the test output on Linux.
            string targetName = dirName switch
            {
                "_synth_nm" => "node_modules",
                "_synth_dot_git" => ".git",
                _ => dirName,
            };
            CopyDirectory(dir, Path.Combine(destination, targetName));
        }
    }

    [Fact]
    public async Task Scans_recognized_lockfiles_and_ignores_node_modules()
    {
        string root = CopyFixtureTree();
        try
        {
            LocalFolderScanner scanner = NewScanner();
            Source source = new()
            {
                Id = 42,
                Type = SourceType.LocalFolder,
                ConfigJson = JsonSerializer.Serialize(new { path = root }),
            };

            ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

            result.Success.Should().BeTrue(result.Error);
            result.Snapshot.Should().NotBeNull();
            result.Snapshot!.SourceId.Should().Be(42);
            result.Snapshot.ItemCount.Should().Be(result.Items.Count);

            result
                .Items.Should()
                .Contain(item => item.Name == "lodash" && item.Ecosystem == Ecosystem.Npm);
            result
                .Items.Should()
                .Contain(item =>
                    item.Name == "monolog/monolog" && item.Ecosystem == Ecosystem.Composer
                );
            result
                .Items.Should()
                .Contain(item => item.Name == "psr/log" && item.Ecosystem == Ecosystem.Composer);

            result.Items.Should().NotContain(item => item.Name == "ghost");

            foreach (InventoryItem item in result.Items)
                item.SnapshotId.Should().Be(result.Snapshot.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ContentsSha_is_stable_across_rescans()
    {
        string root = CopyFixtureTree();
        try
        {
            LocalFolderScanner scanner = NewScanner();
            Source source = new()
            {
                Id = 1,
                Type = SourceType.LocalFolder,
                ConfigJson = JsonSerializer.Serialize(new { path = root }),
            };

            ScanResult first = await scanner.ScanAsync(source, CancellationToken.None);
            ScanResult second = await scanner.ScanAsync(source, CancellationToken.None);

            first.Success.Should().BeTrue();
            second.Success.Should().BeTrue();
            first.Snapshot!.ContentsSha.Should().NotBeEmpty();
            second.Snapshot!.ContentsSha.Should().Be(first.Snapshot.ContentsSha);
            first.Snapshot.Id.Should().NotBe(second.Snapshot.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Custom_ignore_globs_replace_defaults()
    {
        string root = CopyFixtureTree();
        try
        {
            LocalFolderScanner scanner = NewScanner();
            Source source = new()
            {
                Id = 3,
                Type = SourceType.LocalFolder,
                ConfigJson = JsonSerializer.Serialize(
                    new { path = root, ignoreGlobs = new[] { ".git" } }
                ),
            };

            ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Items.Should().Contain(item => item.Name == "ghost");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ManifestPath_is_set_to_relative_forward_slash_path_for_root_lockfile()
    {
        string root = CopyFixtureTree();
        try
        {
            LocalFolderScanner scanner = NewScanner();
            Source source = new()
            {
                Id = 10,
                Type = SourceType.LocalFolder,
                ConfigJson = JsonSerializer.Serialize(new { path = root }),
            };

            ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

            result.Success.Should().BeTrue(result.Error);
            InventoryItem npmItem = result.Items.First(item =>
                item.Ecosystem == Ecosystem.Npm && item.Name == "lodash"
            );
            npmItem.ManifestPath.Should().Be("package-lock.json");

            InventoryItem composerItem = result.Items.First(item =>
                item.Ecosystem == Ecosystem.Composer
            );
            composerItem.ManifestPath.Should().Be("composer.lock");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ManifestPath_uses_forward_slashes_for_subdirectory_manifests()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "shield-manifestpath-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        string subDir = Path.Combine(root, "packages", "core");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(
            Path.Combine(subDir, "package-lock.json"),
            "{\"lockfileVersion\":3,\"packages\":{\"\":{\"dependencies\":{\"react\":\"^18.0.0\"}},\"node_modules/react\":{\"version\":\"18.2.0\"}}}"
        );
        try
        {
            LocalFolderScanner scanner = NewScanner();
            Source source = new()
            {
                Id = 11,
                Type = SourceType.LocalFolder,
                ConfigJson = JsonSerializer.Serialize(new { path = root }),
            };

            ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

            result.Success.Should().BeTrue(result.Error);
            InventoryItem item = result.Items.Should().ContainSingle().Subject;
            item.ManifestPath.Should().Be("packages/core/package-lock.json");
            item.ManifestPath.Should().NotContain("\\");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_path_returns_failure()
    {
        LocalFolderScanner scanner = NewScanner();
        Source source = new()
        {
            Id = 5,
            Type = SourceType.LocalFolder,
            ConfigJson = "{}",
        };

        ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path");
    }

    [Fact]
    public async Task Scan_populates_DetectedRemote_from_dot_git_config()
    {
        string root = CopyFixtureTree();
        try
        {
            LocalFolderScanner scanner = NewScanner();
            Source source = new()
            {
                Id = 7,
                Type = SourceType.LocalFolder,
                ConfigJson = JsonSerializer.Serialize(new { path = root }),
            };

            ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

            result.Success.Should().BeTrue(result.Error);
            source.DetectedRemote.Should().NotBeNullOrEmpty();
            DetectedRemote? parsed = JsonSerializer.Deserialize<DetectedRemote>(
                source.DetectedRemote!
            );
            parsed.Should().NotBeNull();
            parsed!.Host.Should().Be("github.com");
            parsed.Owner.Should().Be("NoMercyLabs");
            parsed.Repo.Should().Be("shield");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
