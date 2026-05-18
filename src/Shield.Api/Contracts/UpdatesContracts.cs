using Shield.Api.Services.Updates;
using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record UpdateRow(
    int Id,
    int SourceId,
    string SourceName,
    Ecosystem Ecosystem,
    string EcosystemLabel,
    string Name,
    string CurrentVersion,
    string LatestVersion,
    DateTime? PublishedAt,
    bool IsBreakingMajor,
    bool IsTooYoung,
    DateTime DetectedAt
);

public sealed record RefreshResponse(int Upserts);

public sealed record ApplyUpdatesRequest(
    UpdateApplyScope Scope,
    IReadOnlyList<int>? SourceIds = null,
    bool DryRun = false,
    bool Force = false,
    bool ConfirmProduction = false
);

public sealed record ApplyUpdatesResponse(bool Queued, Guid? JobId, UpdateApplyResult? Preview);
