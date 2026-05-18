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
    string? AdvisorySummary,
    bool IsKev = false,
    DateTime? KevAddedAt = null,
    DateTime? KevDueDate = null,
    double? EpssScore = null,
    double? EpssPercentile = null,
    // Enum names alongside the numeric wire values so API-token consumers (CI bots, scripts)
    // don't have to keep their own lookup table. Numeric stays the source of truth.
    string SeverityName = "",
    string StateName = "",
    string? EcosystemName = null
)
{
    public static FindingResponse From(
        Finding finding,
        string? sourceName = null,
        string? packageName = null,
        string? packageVersion = null,
        Ecosystem? ecosystem = null,
        string? advisoryExternalId = null,
        string? advisorySummary = null,
        bool isKev = false,
        DateTime? kevAddedAt = null,
        DateTime? kevDueDate = null,
        double? epssScore = null,
        double? epssPercentile = null
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
            advisorySummary,
            isKev,
            kevAddedAt,
            kevDueDate,
            epssScore,
            epssPercentile,
            SeverityName: finding.Severity.ToString(),
            StateName: finding.State.ToString(),
            EcosystemName: ecosystem?.ToString()
        );

    // Overload that pulls KEV/EPSS straight off the Advisory — the controller's EnrichAsync
    // already has the advisory in scope so this avoids threading 5 more parameters through
    // every call site.
    public static FindingResponse From(
        Finding finding,
        Advisory? advisory,
        string? sourceName = null,
        string? packageName = null,
        string? packageVersion = null,
        Ecosystem? ecosystem = null
    ) =>
        From(
            finding,
            sourceName,
            packageName ?? advisory?.PackageName,
            packageVersion,
            ecosystem ?? advisory?.Ecosystem,
            advisory?.ExternalId,
            advisory?.Summary,
            advisory?.IsKev ?? false,
            advisory?.KevAddedAt,
            advisory?.KevDueDate,
            advisory?.EpssScore,
            advisory?.EpssPercentile
        );
}

public sealed record FindingDetailResponse(
    FindingResponse Finding,
    Advisory? Advisory,
    InventoryItem? Item,
    SourceType? SourceType,
    FixSuggestionResponse? FixSuggestion,
    // True when the caller has Triage permission on this finding's source. UI hides the
    // Ack / Resolve / Suppress buttons when false instead of letting the server 403.
    bool CanTriage = false
);

public sealed record FixEligibility(bool Eligible, string? Reason);

public sealed record FixSuggestionResponse(
    string PackageName,
    string CurrentVersion,
    string SuggestedVersion,
    string? Notes,
    FixEligibility PrEligibility,
    FixEligibility AutoEligibility
);

public sealed record ApplyFixRequest(string Strategy);

public sealed record ApplyFixResponse(
    bool Success,
    IReadOnlyList<string> ChangedFiles,
    string? FollowUpCommand,
    string? PullRequestUrl,
    string? Reason,
    IReadOnlyList<string>? CleanedFiles = null,
    IReadOnlyList<string>? CleanedDirectories = null
);

public sealed record SuppressFindingRequest(string Reason);

public sealed record FindingsPage(
    IReadOnlyList<FindingResponse> Items,
    int Total,
    int Page,
    int PageSize
);

public sealed record BulkFindingsRequest(IReadOnlyList<Guid> FindingIds);

public sealed record BulkSuppressRequest(IReadOnlyList<Guid> FindingIds, string Reason);

public sealed record BulkFindingsResponse(int Updated, IReadOnlyList<Guid> NotFound);
