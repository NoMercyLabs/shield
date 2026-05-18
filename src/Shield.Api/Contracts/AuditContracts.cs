namespace Shield.Api.Contracts;

public sealed record AuditEntryResponse(
    Guid Id,
    DateTime At,
    Guid? ActorUserId,
    string ActorName,
    // GitHub login of the actor when known — preferred display over the local username because
    // every collaborator recognises their teammate's public handle, not the Shield account name.
    string? ActorLogin,
    string? ActorAvatarUrl,
    string Action,
    string TargetType,
    string TargetId,
    // Friendly label for the target (Source.Name, Invite.Email, etc.) — null when the type
    // doesn't have an obvious display string. Falls back to TargetId in the UI.
    string? TargetLabel,
    string? DetailsJson,
    string? RemoteIp,
    // True when a registered IAuditUndoHandler exists for Action AND BeforeJson is populated
    // — i.e. clicking Undo will actually roll the change back. UI hides the button otherwise.
    bool IsReversible,
    // Populated once the entry has been undone via /api/audit/{id}/undo. UI greys the row
    // and labels it "reversed" so a user doesn't try the button twice.
    DateTime? ReversedAt,
    Guid? ReversedByEntryId
);

public sealed record AuditPage(
    IReadOnlyList<AuditEntryResponse> Items,
    int Total,
    int Page,
    int PageSize
);

// Result of /api/audit/{id}/undo. Includes the new audit entry id so the SPA can link
// the timeline rows.
public sealed record AuditUndoResponse(
    bool Success,
    string Summary,
    Guid? NewAuditEntryId,
    string? Error = null
);
