namespace Shield.Api.Contracts;

public sealed record AuditEntryResponse(
    Guid Id,
    DateTime At,
    Guid? ActorUserId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    string? DetailsJson,
    string? RemoteIp
);

public sealed record AuditPage(
    IReadOnlyList<AuditEntryResponse> Items,
    int Total,
    int Page,
    int PageSize
);
