namespace Shield.Api.Services.Findings;

public interface IAnomalyDetector
{
    // Diff the newly persisted snapshot against the immediately-prior snapshot for the
    // same source. For every added item that trips a supply-chain heuristic, persist a
    // synthetic Advisory (Feed.NpmRegistry, narrow exact-version range) and enqueue the
    // matcher. Returns the number of synthetic advisories written. No-ops (returns 0)
    // when this is the source's first snapshot — first-scan additions aren't anomalies.
    Task<int> AnalyzeNewSnapshotAsync(int sourceId, Guid newSnapshotId, CancellationToken ct);

    // Pure-function evaluation surface. Takes a precomputed popular-names set per
    // ecosystem — callers diff'ing many items in the same ecosystem should load this
    // once and reuse it instead of paying the query per item.
    AnomalyFlags Evaluate(
        Ecosystem ecosystem,
        string name,
        string version,
        PackageMeta? current,
        PackageMeta? priorVersionMeta,
        DateTime nowUtc,
        IReadOnlySet<string> popularNamesInEcosystem
    );

    // Legacy overload — no typosquat detection (empty popular set). Kept so existing
    // callers in the diff endpoint compile until they're updated to load the popular set.
    AnomalyFlags Evaluate(
        Ecosystem ecosystem,
        string name,
        string version,
        PackageMeta? current,
        PackageMeta? priorVersionMeta,
        DateTime nowUtc
    );
}
