namespace Shield.Api.Services.Security;

// Records admin-significant actions to the AuditEntries table. Scoped so it can pull the
// current HttpContext (actor + remote IP) and write through the request-scoped DbContext.
public interface IAuditLogger
{
    // Returns the new entry id so callers can chain (e.g. an undo reversal entry linking
    // back to the original).
    Task<Guid> RecordAsync(
        string action,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken ct = default
    );

    // Reversible-write variant: captures the row's BEFORE and AFTER state so a later
    // /api/audit/{id}/undo can roll the change back. Caller passes serializable shapes that
    // the matching AuditUndoHandler can rehydrate.
    Task<Guid> RecordWriteAsync(
        string action,
        string targetType,
        string targetId,
        object? before,
        object? after,
        object? details = null,
        CancellationToken ct = default
    );
}
