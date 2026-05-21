using Shield.Api.Services.BulkFix;

namespace Shield.Api.Contracts;

public sealed record BulkApplyRequest(
    bool DryRun = false,
    int? MaxPackages = null,
    bool Force = false,
    bool AllowMajorBumps = false,
    bool ConfirmProduction = false,
    // Acknowledge that Dependabot already has open PRs in the same repo and proceed
    // anyway. The orchestrator returns DependabotConflictAcknowledgeRequired (409) the
    // first time around; the SPA re-submits with this set to true after the user clicks
    // "Continue anyway" in the conflict modal.
    bool AcknowledgeDependabotConflict = false
);

public sealed record SetAutoFixModeRequest(AutoFixMode AutoFixMode);

public sealed record SetIsProductionRequest(bool IsProduction);

// Mirrors BulkApplyResult from BulkFixApplier for API consumers.
public sealed record BulkApplyResponse(
    bool DryRun,
    string? PullRequestUrl,
    IReadOnlyList<BulkApplyEntry> Entries,
    IReadOnlyList<BulkApplyError> Errors,
    string? ReusedBranch,
    IReadOnlyList<BulkApplyEntry>? MajorBumps = null,
    IReadOnlyList<BulkApplyWarning>? Warnings = null
);

public sealed record BulkApplyWarning(string PackageName, string Message);
