namespace Shield.Core.Domain;

// Persistent on-disk queue row. Survives restart so a crash mid-drain doesn't lose the work.
// One row per ScanNow / bulk-add request — never coalesced; the worker dedups in-flight via
// per-source serialisation, not by collapsing rows here.
public class ScanQueueEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SourceId { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }

    // Set when a scanner hits a rate-limit. The worker skips this row until now > DeferredUntil.
    // Null means "ready to run". This is NOT the same as failure — the source is not broken.
    public DateTime? DeferredUntil { get; set; }
}
