namespace Shield.Api.Services.Updates;

// Opens a PR per source bumping every selected outdated dependency. Two scopes:
//   - Latest: every PackageUpdate row that's open + the source supports automatic PRs
//   - LatestMinor: same, but excludes IsBreakingMajor rows
//
// Respects the source's MinPackageAgeHours gate by default — packages whose latest version is
// younger than the gate are skipped UNLESS they have an open Finding (advisory-listed packages
// always ship, regardless of age, since "leave the compromised version in" is worse than "ship
// a brand-new advisory fix").
//
// One PR per source — each source has a different git remote, so cross-repo bumps need to fan
// out. Returns per-source results so the SPA can show partial success when some sources fail.
public interface IUpdateApplier
{
    // Optional callback fires after each source's outcome lands — used by the async worker to
    // broadcast progress to SignalR clients between sources. Pass null when the caller wants a
    // single batch result with no streaming.
    Task<UpdateApplyResult> ApplyAsync(
        UpdateApplyRequest request,
        Func<SourceApplyOutcome, Task>? onSourceCompleted,
        CancellationToken ct
    );
}

public enum UpdateApplyScope
{
    Latest = 0,
    LatestMinor = 1,
}

public sealed record UpdateApplyRequest(
    UpdateApplyScope Scope,
    IReadOnlyList<int>? SourceIds = null,
    bool DryRun = false,
    bool Force = false,
    bool ConfirmProduction = false
);

public sealed record UpdateApplyResult(IReadOnlyList<SourceApplyOutcome> Sources);

public sealed record SourceApplyOutcome(
    int SourceId,
    string SourceName,
    string? PullRequestUrl,
    int BumpedCount,
    int SkippedYoungCount,
    int SkippedMajorCount,
    IReadOnlyList<string> Errors
);
