using System.Text.Json;
using Octokit;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Scanners;

namespace Shield.Api.Services;

public sealed record ApplyFixResult(
    bool Success,
    IReadOnlyList<string> ChangedFiles,
    string? FollowUpCommand,
    string? PullRequestUrl,
    string? Reason
);

public interface IFixApplier
{
    Task<ApplyFixResult> ApplyLocalAsync(
        Source source,
        InventoryItem item,
        FixSuggestion suggestion,
        CancellationToken ct
    );

    Task<ApplyFixResult> ApplyPullRequestAsync(
        Source source,
        InventoryItem item,
        Advisory advisory,
        FixSuggestion suggestion,
        CancellationToken ct
    );
}

public sealed class FixApplier : IFixApplier
{
    private readonly Dictionary<Ecosystem, IManifestEditor> _editors;
    private readonly IOAuthTokenAccessor _tokenAccessor;
    private readonly ProductHeaderValue _productHeader = new("Shield");

    public FixApplier(IEnumerable<IManifestEditor> editors, IOAuthTokenAccessor tokenAccessor)
    {
        _editors = editors.ToDictionary(editor => editor.Ecosystem);
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

        if (!_editors.TryGetValue(item.Ecosystem, out IManifestEditor? editor))
            return Task.FromResult(
                Failure($"No manifest editor registered for ecosystem '{item.Ecosystem}'.")
            );

        ManifestEditOutcome outcome = editor.Apply(
            config.Path,
            item.Name,
            suggestion.SuggestedVersion
        );
        if (outcome.UnsupportedReason is not null)
            return Task.FromResult(Failure(outcome.UnsupportedReason));

        return Task.FromResult(
            new ApplyFixResult(
                Success: true,
                ChangedFiles: outcome.ChangedFiles,
                FollowUpCommand: outcome.FollowUpCommand,
                PullRequestUrl: null,
                Reason: null
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

        if (!_editors.TryGetValue(item.Ecosystem, out IManifestEditor? editor))
            return Failure($"No manifest editor registered for ecosystem '{item.Ecosystem}'.");

        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(config.Token))
            token = config.Token;
        if (string.IsNullOrEmpty(token))
            return Failure(
                "No GitHub OAuth token available. Connect GitHub in Settings or provide a per-source PAT."
            );

        GitHubClient client = new(_productHeader) { Credentials = new Credentials(token) };

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
            // Pull just the manifest files we need by ecosystem. Phase 1 ships npm + nuget +
            // composer paths; other ecosystems get told to use the local strategy or wait.
            IReadOnlyList<string> manifestFiles = ManifestFilesFor(item.Ecosystem, item.Name);
            if (manifestFiles.Count == 0)
                return Failure($"PR strategy not yet supported for ecosystem '{item.Ecosystem}'.");

            List<NewTreeItem> treeItems = new();
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

            ManifestEditOutcome outcome = editor.Apply(
                workRoot,
                item.Name,
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
                    new NewBlob { Content = content, Encoding = EncodingType.Utf8 }
                );
                treeItems.Add(
                    new NewTreeItem
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
                    new NewReference($"refs/heads/{branchName}", created.Sha)
                );
            }
            catch (ApiValidationException)
            {
                // Branch already exists — fast-forward it to the new commit.
                await client.Git.Reference.Update(
                    repo.Id,
                    $"heads/{branchName}",
                    new ReferenceUpdate(created.Sha, force: true)
                );
            }

            string body = BuildPullRequestBody(advisory, item, suggestion);
            PullRequest pr = await client.PullRequest.Create(
                repo.Id,
                new NewPullRequest(commitMessage, branchName, defaultBranch) { Body = body }
            );

            return new ApplyFixResult(
                Success: true,
                ChangedFiles: outcome
                    .ChangedFiles.Select(file =>
                        Path.GetRelativePath(workRoot, file).Replace('\\', '/')
                    )
                    .ToArray(),
                FollowUpCommand: null,
                PullRequestUrl: pr.HtmlUrl,
                Reason: null
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

    private static IReadOnlyList<string> ManifestFilesFor(
        Ecosystem ecosystem,
        string packageName
    ) =>
        ecosystem switch
        {
            Ecosystem.Npm => new[] { "package.json" },
            Ecosystem.Composer => new[] { "composer.json" },
            // For NuGet/Gradle the editor needs the full tree; without local clone we can't
            // discover csproj/build.gradle paths reliably — defer in Phase 1.
            _ => Array.Empty<string>(),
        };

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
        return new string(buffer[..written]);
    }

    private static ApplyFixResult Failure(string reason) =>
        new(
            Success: false,
            ChangedFiles: Array.Empty<string>(),
            FollowUpCommand: null,
            PullRequestUrl: null,
            Reason: reason
        );
}
