namespace Shield.Api.Services.BulkFix;

public sealed record BulkApplyEntry(
    string PackageName,
    string CurrentVersion,
    string SuggestedVersion,
    string ManifestPath,
    IReadOnlyList<string> AdvisoryIds,
    Ecosystem Ecosystem = Ecosystem.Npm
);

public sealed record BulkApplyError(string PackageName, string Reason);

public sealed record BulkApplyResult(
    bool DryRun,
    string? PullRequestUrl,
    IReadOnlyList<BulkApplyEntry> Entries,
    IReadOnlyList<BulkApplyError> Errors,
    string? ReusedBranch,
    IReadOnlyList<BulkApplyEntry> MajorBumps,
    IReadOnlyList<Contracts.BulkApplyWarning> Warnings
);
