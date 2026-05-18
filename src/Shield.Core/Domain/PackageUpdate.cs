namespace Shield.Core.Domain;

// One row per (SourceId, Ecosystem, Name) the UpdateScannerWorker observes as having a newer
// published version available on its registry. Distinct from Finding (which is advisory-driven):
// a PackageUpdate is "your dep is out of date" — no security implication unless an Advisory
// also exists, in which case the BulkFixApplier security path takes precedence.
public class PackageUpdate
{
    public int Id { get; set; }
    public int SourceId { get; set; }

    // InventoryItem id at time of detection. Null when the source's snapshot rolled forward
    // and the original row was deleted — keeps the row visible until the next scan.
    public int? InventoryItemId { get; set; }

    public Ecosystem Ecosystem { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;

    // Registry-reported publish time for LatestVersion. Drives the 48h safeguard gate.
    public DateTime? PublishedAt { get; set; }

    // True when LatestVersion's major component exceeds CurrentVersion's. The apply UI requires
    // an explicit opt-in for these, mirroring the BulkFixApplier major-bump confirmation.
    public bool IsBreakingMajor { get; set; }

    public DateTime DetectedAt { get; set; }

    // Set when an operator opens a PR for this update via /api/updates/apply. Subsequent
    // scans skip applied rows unless the latest registry version advanced further.
    public DateTime? AppliedAt { get; set; }
    public string? AppliedPullRequestUrl { get; set; }
}
