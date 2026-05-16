namespace Shield.Core.Domain;

public class Finding
{
    public Guid Id { get; set; }
    public int SourceId { get; set; }
    public int InventoryItemId { get; set; }
    public Guid AdvisoryRefId { get; set; }
    public Severity Severity { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public FindingState State { get; set; }
    public string DedupKey { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
