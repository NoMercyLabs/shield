namespace Shield.Api.Services.Security.Snapshots;

// Serializable shape of every mutable Source field. Lives in AuditEntry.BeforeJson /
// AfterJson and feeds the source.update undo handler. Excludes server-managed columns
// (Id, CreatedAt, LastScannedAt, LastError, DetectedRemote, LastBulkApplyAt, …) since
// undo never restores those — they're either immutable or write-only telemetry.
public sealed record SourceSnapshot(
    string Name,
    string ConfigJson,
    TimeSpan ScanInterval,
    bool Enabled,
    AutoFixMode AutoFixMode,
    bool IsProduction,
    int MinPackageAgeHours
);
