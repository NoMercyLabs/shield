using System.Text.Json;
using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Composer;
using Shield.Parsers.Gradle;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Scanners;
using Xunit;

namespace Shield.Scanners.Tests;

public class LocalFolderScannerTests
{
    static ParserRegistry NewParserRegistry() =>
        new(new NpmLockParser(), new NugetLockParser(), new ComposerLockParser(), new GradleLockfileParser());

    static LocalFolderScanner NewScanner() => new(NewParserRegistry());

    static string CopyFixtureTree()
    {
        string fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string tempRoot = Path.Combine(Path.GetTempPath(), "shield-scanner-tests-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(fixtureRoot, tempRoot);
        return tempRoot;
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
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

            result.Items.Should().Contain(item => item.Name == "lodash" && item.Ecosystem == Ecosystem.Npm);
            result.Items.Should().Contain(item => item.Name == "monolog/monolog" && item.Ecosystem == Ecosystem.Composer);
            result.Items.Should().Contain(item => item.Name == "psr/log" && item.Ecosystem == Ecosystem.Composer);

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
                ConfigJson = JsonSerializer.Serialize(new
                {
                    path = root,
                    ignoreGlobs = new[] { ".git" },
                }),
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
}
