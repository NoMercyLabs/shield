namespace Shield.Core.Domain;

// A single per-source ACL grant. Exactly one of UserId or GroupId is populated; the other
// is null. Direct grants override nothing — an effective Level is the max of all rows that
// reach the principal (direct + via every group they belong to).
public sealed class SourceAccess
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public Guid? UserId { get; set; }
    public int? GroupId { get; set; }
    public SourceAccessLevel Level { get; set; }
    public DateTime GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
}
