using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record FindingResponse(
    Guid Id,
    int SourceId,
    string? SourceName,
    int InventoryItemId,
    Guid AdvisoryRefId,
    Severity Severity,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    FindingState State,
    string DedupKey,
    string? Notes,
    string? PackageName,
    string? PackageVersion,
    Ecosystem? Ecosystem,
    string? AdvisoryExternalId,
    string? AdvisorySummary
)
{
    public static FindingResponse From(
        Finding finding,
        string? sourceName = null,
        string? packageName = null,
        string? packageVersion = null,
        Ecosystem? ecosystem = null,
        string? advisoryExternalId = null,
        string? advisorySummary = null
    ) =>
        new(
            finding.Id,
            finding.SourceId,
            sourceName,
            finding.InventoryItemId,
            finding.AdvisoryRefId,
            finding.Severity,
            finding.FirstSeenAt,
            finding.LastSeenAt,
            finding.State,
            finding.DedupKey,
            finding.Notes,
            packageName,
            packageVersion,
            ecosystem,
            advisoryExternalId,
            advisorySummary
        );
}

public sealed record FindingDetailResponse(
    FindingResponse Finding,
    Advisory? Advisory,
    InventoryItem? Item,
    SourceType? SourceType,
    FixSuggestionResponse? FixSuggestion
);

public sealed record FixSuggestionResponse(
    string PackageName,
    string CurrentVersion,
    string SuggestedVersion,
    string? Notes
);

public sealed record ApplyFixRequest(string Strategy);

public sealed record ApplyFixResponse(
    bool Success,
    IReadOnlyList<string> ChangedFiles,
    string? FollowUpCommand,
    string? PullRequestUrl,
    string? Reason
);

public sealed record SuppressFindingRequest(string Reason);

public sealed record FindingsPage(
    IReadOnlyList<FindingResponse> Items,
    int Total,
    int Page,
    int PageSize
);
