using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record SnapshotSummary(
    Guid Id,
    DateTime TakenAt,
    string ContentsSha,
    int ItemCount,
    IReadOnlyDictionary<Ecosystem, int> Ecosystems
);

public sealed record SnapshotListItem(
    Guid Id,
    DateTime TakenAt,
    string ContentsSha,
    int ItemCount,
    Guid? PrevSnapshotId
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
