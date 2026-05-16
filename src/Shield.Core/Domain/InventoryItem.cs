namespace Shield.Core.Domain;

public class InventoryItem
{
    public int Id { get; set; }
    public Guid SnapshotId { get; set; }
    public Ecosystem Ecosystem { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ParentChain { get; set; } = "[]";
    public bool IsDirect { get; set; }
    public InventorySnapshot? Snapshot { get; set; }
}
