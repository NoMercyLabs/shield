using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Octokit;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Composer;
using Shield.Parsers.Go;
using Shield.Parsers.Gradle;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Parsers.Python;
using Shield.Parsers.Rust;
using Shield.Scanners;
using Xunit;

namespace Shield.Scanners.Tests;

public class GitHubRepoScannerTests
{
    const string PackageLockJson = """
        {
          "name": "demo",
          "version": "1.0.0",
          "lockfileVersion": 3,
          "requires": true,
          "packages": {
            "": { "name": "demo", "version": "1.0.0", "dependencies": { "lodash": "^4.17.21" } },
            "node_modules/lodash": {
              "version": "4.17.21",
              "resolved": "https://registry.npmjs.org/lodash/-/lodash-4.17.21.tgz",
              "integrity": "sha512-aaa"
            }
          }
        }
        """;

    static ParserRegistry NewParserRegistry() =>
        new(
            new NpmLockParser(),
            new NugetLockParser(),
            new ComposerLockParser(),
            new GradleLockfileParser(),
            new PythonLockParser(),
            new GoLockParser(),
            new RustLockParser()
        );

    [Fact]
    public async Task Scans_repo_tree_and_parses_recognized_blob()
    {
        IGitHubClient client = Substitute.For<IGitHubClient>();
        IRepositoriesClient repoClient = Substitute.For<IRepositoriesClient>();
        IGitDatabaseClient gitClient = Substitute.For<IGitDatabaseClient>();
        IReferencesClient referencesClient = Substitute.For<IReferencesClient>();
        ITreesClient treesClient = Substitute.For<ITreesClient>();
        IBlobsClient blobsClient = Substitute.For<IBlobsClient>();

        client.Repository.Returns(repoClient);
        client.Git.Returns(gitClient);
        gitClient.Reference.Returns(referencesClient);
        gitClient.Tree.Returns(treesClient);
        gitClient.Blob.Returns(blobsClient);

        const long repoId = 7L;
        Repository repo = BuildRepo(repoId);
        Reference reference = BuildReference("refs/heads/main", "sha-commit");
        TreeItem lockfileItem = BuildTreeItem("apps/web/package-lock.json", "blob-sha-1", TreeType.Blob);
        TreeItem readmeItem = BuildTreeItem("README.md", "blob-sha-2", TreeType.Blob);
        TreeResponse tree = BuildTreeResponse("tree-sha", new[] { lockfileItem, readmeItem });
        Blob lockfileBlob = BuildBlob(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(PackageLockJson)),
            EncodingType.Base64,
            "blob-sha-1"
        );

        repoClient.Get("nomercylabs", "shield").Returns(repo);
        referencesClient.Get(repoId, "heads/main").Returns(reference);
        treesClient.GetRecursive(repoId, "sha-commit").Returns(tree);
        blobsClient.Get(repoId, "blob-sha-1").Returns(lockfileBlob);

        GitHubRepoScanner scanner = new(client, NewParserRegistry());
        Source source = new()
        {
            Id = 11,
            Type = SourceType.GithubRepo,
            ConfigJson = JsonSerializer.Serialize(new
            {
                owner = "nomercylabs",
                repo = "shield",
                branch = "main",
            }),
        };

        ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.SourceId.Should().Be(11);
        result.Items.Should().Contain(item => item.Name == "lodash" && item.Version == "4.17.21");

        await blobsClient.DidNotReceive().Get(repoId, "blob-sha-2");
    }

    [Fact]
    public async Task Missing_owner_or_repo_returns_failure()
    {
        IGitHubClient client = Substitute.For<IGitHubClient>();
        GitHubRepoScanner scanner = new(client, NewParserRegistry());

        Source source = new()
        {
            Id = 1,
            Type = SourceType.GithubRepo,
            ConfigJson = "{}",
        };

        ScanResult result = await scanner.ScanAsync(source, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("owner");
    }

    static Repository BuildRepo(long id) =>
        new(
            url: "https://api.github.com/repos/nomercylabs/shield",
            htmlUrl: "https://github.com/nomercylabs/shield",
            cloneUrl: "https://github.com/nomercylabs/shield.git",
            gitUrl: "git://github.com/nomercylabs/shield.git",
            sshUrl: "git@github.com:nomercylabs/shield.git",
            svnUrl: "https://github.com/nomercylabs/shield",
            mirrorUrl: null,
            archiveUrl: "https://api.github.com/repos/nomercylabs/shield/{archive_format}{/ref}",
            id: id,
            nodeId: "node",
            owner: null!,
            name: "shield",
            fullName: "nomercylabs/shield",
            isTemplate: false,
            description: "",
            homepage: "",
            language: "C#",
            @private: false,
            fork: false,
            forksCount: 0,
            stargazersCount: 0,
            watchersCount: 0,
            defaultBranch: "main",
            openIssuesCount: 0,
            pushedAt: null,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            permissions: null!,
            parent: null!,
            source: null!,
            license: null!,
            hasDiscussions: false,
            hasIssues: true,
            hasWiki: false,
            hasDownloads: false,
            allowRebaseMerge: null,
            allowSquashMerge: null,
            allowMergeCommit: null,
            hasPages: false,
            subscribersCount: 0,
            size: 0,
            archived: false,
            topics: Array.Empty<string>(),
            deleteBranchOnMerge: null,
            visibility: RepositoryVisibility.Public,
            allowAutoMerge: null,
            allowUpdateBranch: null,
            webCommitSignoffRequired: null,
            securityAndAnalysis: null!
        );

    static Reference BuildReference(string @ref, string sha)
    {
        TagObject obj = new(
            nodeId: "node-tag",
            url: $"https://api.github.com/repos/x/y/git/refs/{@ref}",
            label: @ref,
            @ref: @ref,
            sha: sha,
            user: null!,
            repository: null!,
            type: TaggedType.Commit
        );
        return new Reference(@ref, "node-ref", $"https://api.github.com/repos/x/y/git/refs/{@ref}", obj);
    }

    static TreeItem BuildTreeItem(string path, string sha, TreeType type) =>
        new(
            path: path,
            mode: "100644",
            type: type,
            size: 100,
            sha: sha,
            url: $"https://api.github.com/repos/x/y/git/blobs/{sha}"
        );

    static TreeResponse BuildTreeResponse(string sha, IReadOnlyList<TreeItem> items) =>
        new(sha, $"https://api.github.com/repos/x/y/git/trees/{sha}", items, false);

    static Blob BuildBlob(string content, EncodingType encoding, string sha) =>
        new(nodeId: "node-blob", content: content, encoding: encoding, sha: sha, size: content.Length);
}
