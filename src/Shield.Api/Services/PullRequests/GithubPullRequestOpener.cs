using System.Text.Json;
using Octokit;
using Shield.Scanners;

namespace Shield.Api.Services.PullRequests;

public sealed class GithubPullRequestOpener : IRepoPullRequestOpener
{
    private static readonly ProductHeaderValue ProductHeader = new("Shield");

    private readonly IOAuthTokenAccessor _tokenAccessor;

    public GithubPullRequestOpener(IOAuthTokenAccessor tokenAccessor)
    {
        _tokenAccessor = tokenAccessor;
    }

    public async Task<RepoPullRequestResult> OpenAsync(
        Source source,
        IReadOnlyList<RepoFileEdit> edits,
        RepoPullRequestSpec spec,
        CancellationToken ct
    )
    {
        if (source.Type != SourceType.GithubRepo)
            return Failure("source", "Pull-request strategy requires a GithubRepo source.");
        if (edits.Count == 0)
            return Failure("manifest", "No manifest files were modified.");

        GitHubRepoConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GitHubRepoConfig>(source.ConfigJson);
        }
        catch (JsonException ex)
        {
            return Failure("source", $"Invalid GithubRepo config: {ex.Message}");
        }
        if (
            config is null
            || string.IsNullOrWhiteSpace(config.Owner)
            || string.IsNullOrWhiteSpace(config.Repo)
        )
            return Failure("source", "GithubRepo config missing 'owner' or 'repo'.");

        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(config.Token))
            token = config.Token;
        if (string.IsNullOrEmpty(token))
            return Failure(
                "auth",
                "No GitHub OAuth token available. Connect GitHub in Settings or provide a per-source PAT."
            );

        GitHubClient client = new(ProductHeader) { Credentials = new(token) };

        Repository repo;
        try
        {
            repo = await client.Repository.Get(config.Owner, config.Repo);
        }
        catch (Exception ex)
        {
            return Failure("repo", $"Failed to fetch repo: {ex.Message}");
        }

        string defaultBranch = string.IsNullOrWhiteSpace(config.Branch)
            ? repo.DefaultBranch
            : config.Branch!;

        Reference baseRef;
        try
        {
            baseRef = await client.Git.Reference.Get(repo.Id, $"heads/{defaultBranch}");
        }
        catch (Exception ex)
        {
            return Failure(
                "branch",
                $"Failed to fetch base branch '{defaultBranch}': {ex.Message}"
            );
        }

        List<NewTreeItem> treeItems = [];
        foreach (RepoFileEdit edit in edits)
        {
            BlobReference blob = await client.Git.Blob.Create(
                repo.Id,
                new() { Content = edit.Content, Encoding = EncodingType.Utf8 }
            );
            treeItems.Add(
                new()
                {
                    Path = edit.Path,
                    Mode = "100644",
                    Type = TreeType.Blob,
                    Sha = blob.Sha,
                }
            );
        }

        NewTree newTree = new() { BaseTree = baseRef.Object.Sha };
        foreach (NewTreeItem treeItem in treeItems)
            newTree.Tree.Add(treeItem);
        TreeResponse tree = await client.Git.Tree.Create(repo.Id, newTree);

        NewCommit newCommit = new(spec.CommitMessage, tree.Sha, baseRef.Object.Sha);
        Commit commit = await client.Git.Commit.Create(repo.Id, newCommit);

        string branchName = $"{spec.BranchPrefix}-{DateTime.UtcNow:yyyyMMdd}";
        bool reusedBranch = false;

        Reference? existingRef = null;
        try
        {
            existingRef = await client.Git.Reference.Get(repo.Id, $"heads/{branchName}");
        }
        catch (NotFoundException)
        {
            existingRef = null;
        }

        if (existingRef is not null)
        {
            reusedBranch = true;
            await client.Git.Reference.Update(
                repo.Id,
                $"heads/{branchName}",
                new(commit.Sha, force: true)
            );
        }
        else
        {
            try
            {
                await client.Git.Reference.Create(
                    repo.Id,
                    new($"refs/heads/{branchName}", commit.Sha)
                );
            }
            catch (ApiValidationException)
            {
                // Race — another process beat us; fall back to suffixed name.
                branchName += $"-{Guid.NewGuid().ToString("n")[..6]}";
                await client.Git.Reference.Create(
                    repo.Id,
                    new($"refs/heads/{branchName}", commit.Sha)
                );
            }
        }

        string? pullRequestUrl = null;
        if (reusedBranch)
        {
            IReadOnlyList<PullRequest> existing = await client.PullRequest.GetAllForRepository(
                repo.Id,
                new PullRequestRequest
                {
                    Head = $"{config.Owner}:{branchName}",
                    State = ItemStateFilter.Open,
                }
            );
            if (existing.Count > 0)
            {
                PullRequest existingPr = await client.PullRequest.Update(
                    repo.Id,
                    existing[0].Number,
                    new() { Body = spec.PrBody, Title = spec.PrTitle }
                );
                pullRequestUrl = existingPr.HtmlUrl;
            }
        }

        if (pullRequestUrl is null)
        {
            PullRequest pr = await client.PullRequest.Create(
                repo.Id,
                new(spec.PrTitle, branchName, defaultBranch) { Body = spec.PrBody }
            );
            pullRequestUrl = pr.HtmlUrl;
        }

        return new(pullRequestUrl, branchName, []);
    }

    private static RepoPullRequestResult Failure(string source, string message) =>
        new(PullRequestUrl: null, BranchName: null, Errors: [new(source, message)]);
}
