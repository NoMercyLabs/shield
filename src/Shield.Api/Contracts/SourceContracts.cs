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
    DateTime UpdatedAt
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
            source.UpdatedAt
        );
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
    int ItemCount
);

public sealed record InventoryItemResponse(
    int Id,
    Ecosystem Ecosystem,
    string Name,
    string Version,
    bool IsDirect,
    string ParentChain
);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize
);

public sealed record ScanQueuedResponse(bool Accepted, DateTime QueuedAt, DateTime? EstimatedCompletion);
