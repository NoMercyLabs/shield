namespace Shield.Api.Services.FixApply;

public sealed record ApplyFixResult(
    bool Success,
    IReadOnlyList<string> ChangedFiles,
    string? FollowUpCommand,
    string? PullRequestUrl,
    string? Reason,
    IReadOnlyList<string> CleanedFiles,
    IReadOnlyList<string> CleanedDirectories
);
