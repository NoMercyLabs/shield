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
    string? RemoteIp
);

public sealed record AuditPage(
    IReadOnlyList<AuditEntryResponse> Items,
    int Total,
    int Page,
    int PageSize
);
