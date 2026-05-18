namespace Shield.Api.Services.Security.Snapshots;

// Serialised role set for a user. Lives in AuditEntry.BeforeJson / AfterJson around
// user.role.changed actions; UserRoleUndoHandler reapplies the captured list verbatim.
public sealed record UserRolesSnapshot(IReadOnlyList<string> Roles);
