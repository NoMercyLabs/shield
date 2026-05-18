using System.Text.Json;
using Octokit;
using Shield.Api.Services.Ecosystems;
using Shield.Api.Services.FixApply;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Scanners;

namespace Shield.Api.Services.FixApply;

public sealed class FixApplier : IFixApplier
{
    private readonly IEcosystemRegistry _ecosystems;
    private readonly IOAuthTokenAccessor _tokenAccessor;
    private readonly ProductHeaderValue _productHeader = new("Shield");

    public FixApplier(IEcosystemRegistry ecosystems, IOAuthTokenAccessor tokenAccessor)
    {
        _ecosystems = ecosystems;
        _tokenAccessor = tokenAccessor;
    }

    public Task<ApplyFixResult> ApplyLocalAsync(
        Source source,
        InventoryItem item,
        FixSuggestion suggestion,
        CancellationToken ct
    )
    {
        LocalFolderConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<LocalFolderConfig>(source.ConfigJson);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(Failure($"Invalid LocalFolder config: {ex.Message}"));
        }
        if (config is null || string.IsNullOrWhiteSpace(config.Path))
            return Task.FromResult(Failure("LocalFolder config missing 'path'."));
        if (!Directory.Exists(config.Path))
            return Task.FromResult(Failure($"Path does not exist: {config.Path}"));

        IEcosystem? ecosystem = _ecosystems.For(item.Ecosystem);
        if (ecosystem is null || !ecosystem.SupportsAutomaticPullRequests)
            return Task.FromResult(
                Failure($"No manifest editor registered for ecosystem '{item.Ecosystem}'.")
            );

        ManifestEditOutcome outcome = ecosystem.Apply(
            config.Path,
            item,
            suggestion.SuggestedVersion
        );
        if (outcome.UnsupportedReason is not null)
            return Task.FromResult(Failure(outcome.UnsupportedReason));

        List<string> cleanedFiles = [];
        List<string> cleanedDirs = [];

        foreach (string lockfile in outcome.CleanedFiles)
        {
            try
            {
                if (File.Exists(lockfile))
                {
                    File.Delete(lockfile);
                    cleanedFiles.Add(lockfile);
                }
            }
            catch
            { /* best effort */
            }
        }

        foreach (string dir in outcome.CleanedDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                    cleanedDirs.Add(dir);
                }
            }
            catch
            { /* best effort */
            }
        }

        return Task.FromResult(
            new ApplyFixResult(
                Success: true,
                ChangedFiles: outcome.ChangedFiles,
                FollowUpCommand: outcome.FollowUpCommand,
                PullRequestUrl: null,
                Reason: null,
                CleanedFiles: cleanedFiles,
                CleanedDirectories: cleanedDirs
            )
        );
    }

    public async Task<ApplyFixResult> ApplyPullRequestAsync(
        Source source,
        InventoryItem item,
        Advisory advisory,
        FixSuggestion suggestion,
        CancellationToken ct
    )
    {
        GitHubRepoConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GitHubRepoConfig>(source.ConfigJson);
        }
        catch (JsonException ex)
        {
            return Failure($"Invalid GithubRepo config: {ex.Message}");
        }
        if (
            config is null
            || string.IsNullOrWhiteSpace(config.Owner)
            || string.IsNullOrWhiteSpace(config.Repo)
        )
            return Failure("GithubRepo config missing 'owner' or 'repo'.");

        IEcosystem? ecosystem = _ecosystems.For(item.Ecosystem);
        if (ecosystem is null || !ecosystem.SupportsAutomaticPullRequests)
            return Failure($"No manifest editor registered for ecosystem '{item.Ecosystem}'.");

        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(config.Token))
            token = config.Token;
        if (string.IsNullOrEmpty(token))
            return Failure(
                "No GitHub OAuth token available. Connect GitHub in Settings or provide a per-source PAT."
            );

        GitHubClient client = new(_productHeader) { Credentials = new(token) };

        Repository repo;
        try
        {
            repo = await client.Repository.Get(config.Owner, config.Repo);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to fetch repo: {ex.Message}");
        }
        string defaultBranch = string.IsNullOrWhiteSpace(config.Branch)
            ? repo.DefaultBranch
            : config.Branch!;

        // Stage manifest edits into a temp working dir mirroring the source root.
        string workRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-apply",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(workRoot);
        try
        {
            // Use the manifest path recorded at scan time when available so monorepo sub-packages
            // and non-root manifests are edited in the right file. Fall back to the ecosystem
            // default for legacy rows that pre-date ManifestPath being populated.
            IReadOnlyList<string> manifestFiles = !string.IsNullOrWhiteSpace(item.ManifestPath)
                ? new[] { item.ManifestPath }
                : new[] { ecosystem.DefaultManifestPath };
            if (manifestFiles.Count == 0)
                return Failure($"PR strategy not yet supported for ecosystem '{item.Ecosystem}'.");

            List<NewTreeItem> treeItems = [];
            Reference baseRef;
            try
            {
                baseRef = await client.Git.Reference.Get(repo.Id, $"heads/{defaultBranch}");
            }
            catch (Exception ex)
            {
                return Failure($"Failed to fetch base branch '{defaultBranch}': {ex.Message}");
            }

            foreach (string relative in manifestFiles)
            {
                string localPath = Path.Combine(workRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                try
                {
                    IReadOnlyList<RepositoryContent> contents =
                        await client.Repository.Content.GetAllContentsByRef(
                            repo.Id,
                            relative,
                            defaultBranch
                        );
                    if (contents.Count == 0)
                        continue;
                    File.WriteAllText(localPath, contents[0].Content);
                }
                catch (NotFoundException)
                {
                    continue;
                }
            }

            ManifestEditOutcome outcome = ecosystem.Apply(
                workRoot,
                item,
                suggestion.SuggestedVersion
            );
            if (outcome.UnsupportedReason is not null)
                return Failure(outcome.UnsupportedReason);
            if (outcome.ChangedFiles.Count == 0)
                return Failure("Manifest editor reported no changes.");

            foreach (string changed in outcome.ChangedFiles)
            {
                string relative = Path.GetRelativePath(workRoot, changed).Replace('\\', '/');
                string content = File.ReadAllText(changed);
                BlobReference blob = await client.Git.Blob.Create(
                    repo.Id,
                    new() { Content = content, Encoding = EncodingType.Utf8 }
                );
                treeItems.Add(
                    new()
                    {
                        Path = relative,
                        Mode = "100644",
                        Type = TreeType.Blob,
                        Sha = blob.Sha,
                    }
                );
            }

            NewTree newTree = new() { BaseTree = baseRef.Object.Sha };
            foreach (NewTreeItem entry in treeItems)
                newTree.Tree.Add(entry);
            TreeResponse tree = await client.Git.Tree.Create(repo.Id, newTree);

            string commitMessage =
                $"chore(deps): bump {item.Name} to {suggestion.SuggestedVersion} (fixes {advisory.ExternalId})";
            NewCommit commit = new(commitMessage, tree.Sha, baseRef.Object.Sha);
            Commit created = await client.Git.Commit.Create(repo.Id, commit);

            string branchName = $"shield/fix-{Slug(item.Name)}-{suggestion.SuggestedVersion}";
            try
            {
                await client.Git.Reference.Create(
                    repo.Id,
                    new($"refs/heads/{branchName}", created.Sha)
                );
            }
            catch (ApiValidationException)
            {
                // Branch already exists — fast-forward it to the new commit.
                await client.Git.Reference.Update(
                    repo.Id,
                    $"heads/{branchName}",
                    new(created.Sha, force: true)
                );
            }

            string body = BuildPullRequestBody(advisory, item, suggestion);
            PullRequest pr = await client.PullRequest.Create(
                repo.Id,
                new(commitMessage, branchName, defaultBranch) { Body = body }
            );

            return new(
                Success: true,
                ChangedFiles: outcome
                    .ChangedFiles.Select(file =>
                        Path.GetRelativePath(workRoot, file).Replace('\\', '/')
                    )
                    .ToArray(),
                FollowUpCommand: null,
                PullRequestUrl: pr.HtmlUrl,
                Reason: null,
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }
        finally
        {
            try
            {
                Directory.Delete(workRoot, recursive: true);
            }
            catch
            { /* best effort */
            }
        }
    }

    private static string BuildPullRequestBody(
        Advisory advisory,
        InventoryItem item,
        FixSuggestion suggestion
    )
    {
        string summary = string.IsNullOrWhiteSpace(advisory.Summary)
            ? advisory.ExternalId
            : advisory.Summary;
        return $"Bumps `{item.Name}` from `{suggestion.CurrentVersion}` to `{suggestion.SuggestedVersion}` "
            + $"to address {advisory.ExternalId}.\n\n"
            + $"{summary}\n\n"
            + $"Suggested by Shield. Severity: {advisory.Severity}.";
    }

    private static string Slug(string raw)
    {
        Span<char> buffer = stackalloc char[raw.Length];
        int written = 0;
        foreach (char ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '.')
                buffer[written++] = char.ToLowerInvariant(ch);
            else
                buffer[written++] = '-';
        }
        return new(buffer[..written]);
    }

    private static ApplyFixResult Failure(string reason) =>
        new(
            Success: false,
            ChangedFiles: [],
            FollowUpCommand: null,
            PullRequestUrl: null,
            Reason: reason,
            CleanedFiles: [],
            CleanedDirectories: []
        );
}
