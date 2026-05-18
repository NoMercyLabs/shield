namespace Shield.Core.Domain;

// Append-only record of admin-significant actions: finding transitions, source/channel
// mutations, settings updates, OAuth connect/disconnect. Written by AuditMiddleware after
// the corresponding controller action returns 2xx. Surfaced to admins via /api/audit.
public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = "system";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string? DetailsJson { get; set; }
    public string? RemoteIp { get; set; }

    // Pre/post-state for reversible writes. BeforeJson is the row's shape BEFORE the action
    // landed (used by undo handlers to roll the row back). AfterJson is what the action
    // wrote — kept alongside for diff rendering in the audit UI. Both null for read /
    // session / informational entries that aren't reversible.
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    // Whether an undo handler exists for this Action AND beforeJson is populated. Set by
    // the writer (not derived). Filters which audit rows render an Undo button.
    public bool IsReversible { get; set; }

    // Once the entry has been reversed via /api/audit/{id}/undo, ReversedAt + ReversedByEntryId
    // point at the audit entry the undo created. Prevents double-undo and surfaces the
    // chain in the timeline.
    public DateTime? ReversedAt { get; set; }
    public Guid? ReversedByEntryId { get; set; }
}
