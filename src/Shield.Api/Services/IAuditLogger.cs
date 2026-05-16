namespace Shield.Api.Services;

// Records admin-significant actions to the AuditEntries table. Scoped so it can pull the
// current HttpContext (actor + remote IP) and write through the request-scoped DbContext.
public interface IAuditLogger
{
    Task RecordAsync(
        string action,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken ct = default
    );
}
