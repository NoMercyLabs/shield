namespace Shield.Api.Services.PullRequests;

// Opens (or updates) a pull request on a GithubRepo source given a pre-built list of file
// content edits. Owns the entire Git/GitHub plumbing — token resolution, repo fetch, base-ref
// lookup, blob/tree/commit creation, branch reuse, PR create-or-update.
//
// Callers (BulkFixApplier for advisory-driven fixes, future ApplyUpdates for non-security bumps)
// supply ready-made file contents and metadata; they never touch Octokit directly.
public interface IRepoPullRequestOpener
{
    Task<RepoPullRequestResult> OpenAsync(
        Source source,
        IReadOnlyList<RepoFileEdit> edits,
        RepoPullRequestSpec spec,
        CancellationToken ct
    );
}

// One file write applied to the working tree before the PR is opened. `Content` is the final
// post-edit content (UTF-8) — the opener writes it verbatim as a blob.
public sealed record RepoFileEdit(string Path, string Content);

public sealed record RepoPullRequestSpec(
    string BranchPrefix,
    string CommitMessage,
    string PrTitle,
    string PrBody
);

public sealed record RepoPullRequestResult(
    string? PullRequestUrl,
    string? BranchName,
    IReadOnlyList<RepoPullRequestError> Errors
);

public sealed record RepoPullRequestError(string Source, string Message);
