using Shield.Core.Domain;

namespace Shield.Core.Results;

public sealed record ScanResult(
    InventorySnapshot? Snapshot,
    IReadOnlyList<InventoryItem> Items,
    bool Success,
    string? Error
)
{
    public static ScanResult Ok(InventorySnapshot snapshot, IReadOnlyList<InventoryItem> items) =>
        new(snapshot, items, true, null);

    public static ScanResult Fail(string error) => new(null, [], false, error);
}
