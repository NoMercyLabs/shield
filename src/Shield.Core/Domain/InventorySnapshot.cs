namespace Shield.Core.Domain;

public class InventorySnapshot
{
    public Guid Id { get; set; }
    public int SourceId { get; set; }
    public DateTime TakenAt { get; set; }
    public string ContentsSha { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public Source? Source { get; set; }
    public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();
}
