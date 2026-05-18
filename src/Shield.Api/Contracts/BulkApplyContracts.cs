using Shield.Api.Services.BulkFix;
using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record BulkApplyRequest(
    bool DryRun = false,
    int? MaxPackages = null,
    bool Force = false,
    bool AllowMajorBumps = false,
    bool ConfirmProduction = false
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
