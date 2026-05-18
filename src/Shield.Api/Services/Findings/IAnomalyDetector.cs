using Shield.Api.Contracts;
using Shield.Core.Domain;

namespace Shield.Api.Services.Findings;

public interface IAnomalyDetector
{
    // Diff the newly persisted snapshot against the immediately-prior snapshot for
    // the same source. For every added item that trips a supply-chain heuristic,
    // persist a synthetic Advisory (Feed.NpmRegistry, narrow exact-version range)
    // and enqueue the matcher. Returns the number of synthetic advisories written.
    // No-ops (returns 0) when this is the source's first snapshot — first-scan
    // additions aren't anomalies, they're the baseline.
    Task<int> AnalyzeNewSnapshotAsync(int sourceId, Guid newSnapshotId, CancellationToken ct);

    // Pure-function evaluation surface used by both the live detector and the diff
    // endpoint. Given the prior + current PackageMeta (and a "now" clock for the
    // BrandNew window), returns the flag set for an item that was just added.
    AnomalyFlags Evaluate(
        Ecosystem ecosystem,
        string name,
        string version,
        PackageMeta? current,
        PackageMeta? priorVersionMeta,
        DateTime nowUtc
    );
}
