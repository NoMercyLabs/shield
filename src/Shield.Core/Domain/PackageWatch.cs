namespace Shield.Core.Domain;

// Per-operator package star. Unique on (UserId, Ecosystem, PackageName) so the same package
// can be watched by multiple operators independently. The dashboard widget joins this
// against Findings/InventoryItems to surface "you depend on this on N sources".
public sealed class PackageWatch
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Ecosystem Ecosystem { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}
