using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record CreateSourceRequest(
    SourceType Type,
    string Name,
    string ConfigJson,
    TimeSpan ScanInterval,
    bool Enabled = true
);

public sealed record UpdateSourceRequest(
    string Name,
    string ConfigJson,
    TimeSpan ScanInterval,
    bool Enabled
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

public sealed record SnapshotSummary(Guid Id, DateTime TakenAt, string ContentsSha, int ItemCount);

public sealed record SourceDetailResponse(SourceResponse Source, SnapshotSummary? LastSnapshot);
