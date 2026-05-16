using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Domain;

namespace Shield.Api.Contracts;

// CreateSourceRequest accepts ConfigJson either as a JSON string ("{\"path\":\"...\"}")
// or a JSON object ({ "path": "..." }). The controller normalises to string on the way in
// so scanners keep their existing string-backed parse path.
public sealed record CreateSourceRequest(
    SourceType Type,
    [Required] string Name,
    JsonElement ConfigJson,
    TimeSpan? ScanInterval = null,
    bool Enabled = true
);

public sealed record UpdateSourceRequest(
    [Required] string Name,
    JsonElement ConfigJson,
    TimeSpan? ScanInterval = null,
    bool Enabled = true
);

public sealed record DetectedRemoteDto(
    string Host,
    string Owner,
    string Repo,
    string RemoteUrl,
    string? Branch
);

public sealed record SourceResponse(
    int Id,
    SourceType Type,
    string Name,
    string ConfigJson,
    TimeSpan ScanInterval,
    DateTime? LastScannedAt,
    string? LastError,
    bool Enabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DetectedRemoteDto? DetectedRemote
)
{
    public static SourceResponse From(Source source) =>
        new(
            source.Id,
            source.Type,
            source.Name,
            source.ConfigJson,
            source.ScanInterval,
            source.LastScannedAt,
            source.LastError,
            source.Enabled,
            source.CreatedAt,
            source.UpdatedAt,
            ParseDetectedRemote(source.DetectedRemote)
        );

    private static readonly JsonSerializerOptions DetectedRemoteOptions = new(
        JsonSerializerDefaults.Web
    );

    private static DetectedRemoteDto? ParseDetectedRemote(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DetectedRemoteDto>(json, DetectedRemoteOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record SnapshotSummary(
    Guid Id,
    DateTime TakenAt,
    string ContentsSha,
    int ItemCount,
    IReadOnlyDictionary<Ecosystem, int> Ecosystems
);

public sealed record SourceDetailResponse(SourceResponse Source, SnapshotSummary? LatestSnapshot);

public sealed record SnapshotListItem(
    Guid Id,
    DateTime TakenAt,
    string ContentsSha,
    int ItemCount,
    Guid? PrevSnapshotId
);

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

public sealed record InventoryItemResponse(
    int Id,
    Ecosystem Ecosystem,
    string Name,
    string Version,
    bool IsDirect,
    string ParentChain
);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ScanQueuedResponse(
    bool Accepted,
    DateTime QueuedAt,
    DateTime? EstimatedCompletion
);

// "Pick from GitHub" bulk-add — one selection per repo the operator ticked. Branch is
// optional; when null the scanner falls back to the repo's default branch.
public sealed record BulkSelection(string Owner, string Name, string? Branch);

public sealed record BulkFromGithubRequest(
    IReadOnlyList<BulkSelection> Selections,
    TimeSpan? DefaultScanInterval = null
);

public sealed record BulkFromGithubResponse(
    int Created,
    int SkippedExisting,
    IReadOnlyList<SourceResponse> Sources
);
