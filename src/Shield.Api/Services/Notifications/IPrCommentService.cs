namespace Shield.Api.Services.Notifications;

// Runs a one-shot scan of a PR head ref, diffs against the latest snapshot on the
// source's tracked branch, looks up advisories for any *newly introduced* packages,
// and posts (or updates) a single Markdown comment on the PR identified by a sentinel.
public interface IPrCommentService
{
    Task<PrCommentResult> ProcessPullRequestAsync(
        string owner,
        string repoName,
        int pullNumber,
        string headRef,
        string baseRef,
        CancellationToken ct
    );
}

public sealed record PrCommentResult(
    bool ScanRan,
    int AddedPackageCount,
    int VulnerableAddedCount,
    long? CommentId,
    string? Reason
);
