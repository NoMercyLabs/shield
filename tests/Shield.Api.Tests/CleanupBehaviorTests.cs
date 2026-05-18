using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Shield.Api.Services.FixApply;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

// Verifies that ApplyLocalAsync deletes lockfiles + installed-package dirs after a successful
// manifest edit, and that ApplyPullRequestAsync never touches the local filesystem.
public sealed class CleanupBehaviorTests
{
    // ------------------------------------------------------------------
    // npm — package-lock.json + node_modules deleted
    // ------------------------------------------------------------------

    [Fact]
    public async Task NpmApplyLocalDeletesPackageLockAndNodeModules()
    {
        string rootPath = TempDir();
        try
        {
            WritePackageJson(rootPath, "lodash", "^4.17.20");
            string lockfile = Path.Combine(rootPath, "package-lock.json");
            string nodeModules = Path.Combine(rootPath, "node_modules");
            await File.WriteAllTextAsync(lockfile, "{}");
            Directory.CreateDirectory(nodeModules);
            await File.WriteAllTextAsync(Path.Combine(nodeModules, "dummy.js"), "");

            ApplyFixResult result = await ApplyLocalAsync(
                Ecosystem.Npm,
                rootPath,
                "lodash",
                "4.17.21"
            );

            result.Success.Should().BeTrue(result.Reason);
            result.CleanedFiles.Should().ContainSingle(file => file.EndsWith("package-lock.json"));
            result.CleanedDirectories.Should().ContainSingle(dir => dir.EndsWith("node_modules"));
            File.Exists(lockfile).Should().BeFalse();
            Directory.Exists(nodeModules).Should().BeFalse();
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    [Fact]
    public async Task NpmApplyLocalDeletesYarnLockAndNodeModules()
    {
        string rootPath = TempDir();
        try
        {
            WritePackageJson(rootPath, "lodash", "^4.17.20");
            string yarnLock = Path.Combine(rootPath, "yarn.lock");
            string nodeModules = Path.Combine(rootPath, "node_modules");
            await File.WriteAllTextAsync(yarnLock, "# yarn");
            Directory.CreateDirectory(nodeModules);

            ApplyFixResult result = await ApplyLocalAsync(
                Ecosystem.Npm,
                rootPath,
                "lodash",
                "4.17.21"
            );

            result.Success.Should().BeTrue(result.Reason);
            result.CleanedFiles.Should().ContainSingle(file => file.EndsWith("yarn.lock"));
            result.CleanedDirectories.Should().ContainSingle(dir => dir.EndsWith("node_modules"));
            File.Exists(yarnLock).Should().BeFalse();
            Directory.Exists(nodeModules).Should().BeFalse();
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    [Fact]
    public async Task NpmApplyLocalDeletesPnpmLockAndNodeModules()
    {
        string rootPath = TempDir();
        try
        {
            WritePackageJson(rootPath, "lodash", "^4.17.20");
            string pnpmLock = Path.Combine(rootPath, "pnpm-lock.yaml");
            string nodeModules = Path.Combine(rootPath, "node_modules");
            await File.WriteAllTextAsync(pnpmLock, "lockfileVersion: 9");
            Directory.CreateDirectory(nodeModules);

            ApplyFixResult result = await ApplyLocalAsync(
                Ecosystem.Npm,
                rootPath,
                "lodash",
                "4.17.21"
            );

            result.Success.Should().BeTrue(result.Reason);
            result.CleanedFiles.Should().ContainSingle(file => file.EndsWith("pnpm-lock.yaml"));
            result.FollowUpCommand.Should().Be("pnpm install");
            File.Exists(pnpmLock).Should().BeFalse();
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    [Fact]
    public async Task NpmApplyLocalNoLockfileOrNodeModulesCleanedListsEmpty()
    {
        string rootPath = TempDir();
        try
        {
            WritePackageJson(rootPath, "lodash", "^4.17.20");

            ApplyFixResult result = await ApplyLocalAsync(
                Ecosystem.Npm,
                rootPath,
                "lodash",
                "4.17.21"
            );

            result.Success.Should().BeTrue(result.Reason);
            result.CleanedFiles.Should().BeEmpty();
            result.CleanedDirectories.Should().BeEmpty();
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    // ------------------------------------------------------------------
    // composer — composer.lock + vendor deleted
    // ------------------------------------------------------------------

    [Fact]
    public async Task ComposerApplyLocalDeletesComposerLockAndVendor()
    {
        string rootPath = TempDir();
        try
        {
            WriteComposerJson(rootPath, "monolog/monolog", "^2.0");
            string lockfile = Path.Combine(rootPath, "composer.lock");
            string vendorDir = Path.Combine(rootPath, "vendor");
            await File.WriteAllTextAsync(lockfile, "{}");
            Directory.CreateDirectory(vendorDir);
            await File.WriteAllTextAsync(Path.Combine(vendorDir, "autoload.php"), "<?php");

            ApplyFixResult result = await ApplyLocalAsync(
                Ecosystem.Composer,
                rootPath,
                "monolog/monolog",
                "2.9.0"
            );

            result.Success.Should().BeTrue(result.Reason);
            result.CleanedFiles.Should().ContainSingle(file => file.EndsWith("composer.lock"));
            result.CleanedDirectories.Should().ContainSingle(dir => dir.EndsWith("vendor"));
            File.Exists(lockfile).Should().BeFalse();
            Directory.Exists(vendorDir).Should().BeFalse();
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    [Fact]
    public async Task ComposerApplyLocalNoLockfileOrVendorCleanedListsEmpty()
    {
        string rootPath = TempDir();
        try
        {
            WriteComposerJson(rootPath, "monolog/monolog", "^2.0");

            ApplyFixResult result = await ApplyLocalAsync(
                Ecosystem.Composer,
                rootPath,
                "monolog/monolog",
                "2.9.0"
            );

            result.Success.Should().BeTrue(result.Reason);
            result.CleanedFiles.Should().BeEmpty();
            result.CleanedDirectories.Should().BeEmpty();
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    // ------------------------------------------------------------------
    // NuGet, Python, Go, Rust, Gradle — no cleanup (empty lists)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(Ecosystem.Nuget)]
    [InlineData(Ecosystem.Python)]
    [InlineData(Ecosystem.Go)]
    [InlineData(Ecosystem.Rust)]
    [InlineData(Ecosystem.Gradle)]
    public void NonCleanupEcosystemEditorReturnsEmptyCleanedLists(Ecosystem ecosystem)
    {
        IManifestEditor editor = BuildEditor(ecosystem);
        InventoryItem item = new()
        {
            Ecosystem = ecosystem,
            Name = "some-package",
            Version = "1.0.0",
            IsDirect = true,
            ParentChain = "[]",
        };

        ManifestEditOutcome outcome = editor.Apply(Path.GetTempPath(), item, "2.0.0");

        outcome.CleanedFiles.Should().BeEmpty();
        outcome.CleanedDirectories.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // PR strategy — never touches local FS
    // ------------------------------------------------------------------

    [Fact]
    public async Task PrStrategyNeverDeletesLocalFiles()
    {
        string rootPath = TempDir();
        try
        {
            // Create files that would be cleaned on the local strategy.
            string lockfile = Path.Combine(rootPath, "package-lock.json");
            string nodeModules = Path.Combine(rootPath, "node_modules");
            await File.WriteAllTextAsync(lockfile, "{}");
            Directory.CreateDirectory(nodeModules);

            // PR strategy requires a GitHub source + token. Without valid Octokit creds the
            // call fails. What matters is that even on a code-path that hits Failure() early,
            // the local files are untouched.
            IFixApplier applier = BuildApplier();
            Source source = new()
            {
                Type = SourceType.GithubRepo,
                Name = "test",
                ConfigJson = JsonSerializer.Serialize(
                    new { owner = "shield-test", repo = "fixture" }
                ),
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            InventoryItem item = new()
            {
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.20",
                IsDirect = true,
                ParentChain = "[]",
            };
            Advisory advisory = new()
            {
                Id = Guid.NewGuid(),
                ExternalId = "TEST-1",
                Ecosystem = Ecosystem.Npm,
                PackageName = "lodash",
                AffectedRangesJson =
                    "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                Severity = Severity.High,
                ReferencesJson = "[]",
                PublishedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                FetchedAt = DateTime.UtcNow,
            };
            FixSuggestion suggestion = new("lodash", "4.17.20", "4.17.21", null);

            // The call will fail (no real GitHub token). We only care that local files survive.
            ApplyFixResult result = await applier.ApplyPullRequestAsync(
                source,
                item,
                advisory,
                suggestion,
                CancellationToken.None
            );

            result.Success.Should().BeFalse();
            File.Exists(lockfile).Should().BeTrue("PR strategy must not touch the local FS");
            Directory
                .Exists(nodeModules)
                .Should()
                .BeTrue("PR strategy must not touch the local FS");
        }
        finally
        {
            Cleanup(rootPath);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string TempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "shield-cleanup-test-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        { /* best effort */
        }
    }

    private static void WritePackageJson(string rootPath, string packageName, string version)
    {
        string content =
            $"{{\n  \"name\": \"fixture\",\n  \"dependencies\": {{\n    \"{packageName}\": \"{version}\"\n  }}\n}}\n";
        File.WriteAllText(Path.Combine(rootPath, "package.json"), content);
    }

    private static void WriteComposerJson(string rootPath, string packageName, string version)
    {
        string content = $"{{\n  \"require\": {{\n    \"{packageName}\": \"{version}\"\n  }}\n}}\n";
        File.WriteAllText(Path.Combine(rootPath, "composer.json"), content);
    }

    private static async Task<ApplyFixResult> ApplyLocalAsync(
        Ecosystem ecosystem,
        string rootPath,
        string packageName,
        string suggestedVersion
    )
    {
        IFixApplier applier = BuildApplier();
        Source source = new()
        {
            Type = SourceType.LocalFolder,
            Name = "test",
            ConfigJson = JsonSerializer.Serialize(new { path = rootPath }),
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        InventoryItem item = new()
        {
            Ecosystem = ecosystem,
            Name = packageName,
            Version = "1.0.0",
            IsDirect = true,
            ParentChain = "[]",
        };
        FixSuggestion suggestion = new(packageName, "1.0.0", suggestedVersion, null);
        return await applier.ApplyLocalAsync(source, item, suggestion, CancellationToken.None);
    }

    private static IFixApplier BuildApplier()
    {
        IOAuthTokenAccessor tokenAccessor = Substitute.For<IOAuthTokenAccessor>();
        IManifestEditor[] editors =
        [
            new NpmManifestEditor(),
            new ComposerManifestEditor(),
            new NugetManifestEditor(),
            new PythonManifestEditor(),
            new GoManifestEditor(),
            new RustManifestEditor(),
            new GradleManifestEditor(),
        ];
        return new FixApplier(editors, tokenAccessor);
    }

    private static IManifestEditor BuildEditor(Ecosystem ecosystem) =>
        ecosystem switch
        {
            Ecosystem.Nuget => new NugetManifestEditor(),
            Ecosystem.Python => new PythonManifestEditor(),
            Ecosystem.Go => new GoManifestEditor(),
            Ecosystem.Rust => new RustManifestEditor(),
            Ecosystem.Gradle => new GradleManifestEditor(),
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem)),
        };
}
