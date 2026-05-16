using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record FindingResponse(
    Guid Id,
    int SourceId,
    int InventoryItemId,
    Guid AdvisoryRefId,
    Severity Severity,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    FindingState State,
    string DedupKey,
    string? Notes
)
{
    public static FindingResponse From(Finding finding) =>
        new(
            finding.Id,
            finding.SourceId,
            finding.InventoryItemId,
            finding.AdvisoryRefId,
            finding.Severity,
            finding.FirstSeenAt,
            finding.LastSeenAt,
            finding.State,
            finding.DedupKey,
            finding.Notes
        );
}

public sealed record FindingDetailResponse(
    FindingResponse Finding,
    Advisory? Advisory,
    InventoryItem? Item
);

public sealed record SuppressFindingRequest(string Reason);

public sealed record FindingsPage(
    IReadOnlyList<FindingResponse> Items,
    int Total,
    int Page,
    int PageSize
);
