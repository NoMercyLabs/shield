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

    // Forward-slash relative path from the source root to the manifest file this item came from.
    // e.g. "package.json", "packages/foo/package.json", "Directory.Packages.props".
    // Null for rows written before this column was added — next scan repopulates them.
    public string? ManifestPath { get; set; }

    public InventorySnapshot? Snapshot { get; set; }
}
