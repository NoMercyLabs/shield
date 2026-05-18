namespace Shield.Api.Contracts;

// Anomaly signals flagged by the supply-chain detector on snapshot-to-snapshot diffs.
// Evaluated only on items added in the newer snapshot — removals/version-bumps don't
// trigger anomaly checks (a known-good package downgrading isn't a supply-chain signal,
// it's a regression bug for a different surface).
[Flags]
public enum AnomalyFlags
{
    None = 0,

    // PackageMeta.PublishedAt within the last 30 days at evaluation time.
    BrandNew = 1,

    // Exactly one maintainer on the added version. Single point of compromise.
    SingleMaintainer = 2,

    // Maintainer set differs from the most-recent prior PackageMeta for the same
    // (ecosystem, name). Covers both additions and replacements.
    NewMaintainerThisVersion = 4,

    // Levenshtein distance <= 2 to a popular package name AND not itself popular.
    Typosquat = 8,

    // PackageMeta.Deprecated true.
    Deprecated = 16,

    // Scoped npm package whose inner name doesn't match the scope
    // (e.g. "@lodash/lodash" — scope `lodash`, inner name `lodash` is the
    // hallmark scope-confusion squat against the unscoped `lodash`).
    HighScopeMismatch = 32,
}

public sealed record InventoryDiffEntry(
    Ecosystem Ecosystem,
    string Name,
    string Version,
    bool IsDirect,
    string? ParentChain,
    AnomalyFlags Anomaly
);

public sealed record InventoryDiffChange(
    Ecosystem Ecosystem,
    string Name,
    string FromVersion,
    string ToVersion,
    bool IsDirect
);

public sealed record SnapshotDiffResponse(
    SnapshotSummary Older,
    SnapshotSummary Newer,
    IReadOnlyList<InventoryDiffEntry> Added,
    IReadOnlyList<InventoryDiffEntry> Removed,
    IReadOnlyList<InventoryDiffChange> VersionChanged
);
