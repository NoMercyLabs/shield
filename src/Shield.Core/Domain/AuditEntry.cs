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
}
